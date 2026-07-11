using AgentCore.Conversation;
using AgentCore.Json;
using AgentCore.LLM;
using AgentCore.LLM.Exceptions;
using AgentCore.Memory;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;

namespace AgentCore;

public interface IAgent
{
    Task<T?> InvokeAsync<T>(IContent input, CancellationToken ct = default);
    IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(IContent input, CancellationToken ct = default);
}

public sealed class LLMAgent : IAgent
{
    private readonly IMemory _memory;
    private readonly ILLM _llm;
    private readonly ITooling _tooling;
    private readonly ITokenCounter _tokenCounter;
    private readonly LLMOptions _baseOptions;
    private readonly AgentConfig _config;
    private readonly ILogger<LLMAgent> _logger;

    public IMemory Memory => _memory;
    public ILLM Llm => _llm;
    public ITooling Tooling => _tooling;
    public ITokenCounter TokenCounter => _tokenCounter;
    public LLMOptions Options => _baseOptions;
    public AgentConfig Config => _config;

    public LLMAgent(
        ILLM llm,
        ITooling tooling,
        IMemory memory,
        ITokenCounter tokenCounter,
        LLMOptions baseOptions,
        AgentConfig config,
        ILogger<LLMAgent>? logger = null)
    {
        _memory = memory;
        _llm = llm;
        _tooling = tooling;
        _tokenCounter = tokenCounter;
        _baseOptions = baseOptions;
        _config = config;
        _logger = logger ?? NullLogger<LLMAgent>.Instance;
    }

    public static AgentBuilder Create(string name = "agent")
        => new AgentBuilder().WithName(name);

    public async Task<T?> InvokeAsync<T>(IContent input, CancellationToken ct = default)
    {
        var response = await InvokeAsyncInternal(input, typeof(T), ct);
        var json = response.ForLlm();
        
        if (string.IsNullOrWhiteSpace(json)) return default;
        return System.Text.Json.JsonSerializer.Deserialize<T>(json);
    }

    private async Task<IContent> InvokeAsyncInternal(
        IContent input, 
        Type? outputType, 
        CancellationToken ct)
    {
        var turnMessages = new List<Message>();

        await foreach (var evt in CoreStreamAsync(input, outputType, turnMessages, ct))
        {
            if (evt is ErrorEvent error)
            {
                throw error.Error;
            }
        }

        var lastAssistantMsg = turnMessages
            .Where(m => m.Role == Role.Assistant)
            .LastOrDefault();

        IContent content = lastAssistantMsg?.Contents.OfType<Text>().LastOrDefault()
            ?? (lastAssistantMsg?.Contents.LastOrDefault() ?? new Text(""));

        return content;
    }

    public IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        IContent input,
        CancellationToken ct = default)
    {
        var turnMessages = new List<Message>();
        return CoreStreamAsync(input, null, turnMessages, ct);
    }

    private LLMOptions BuildLLMOptions(Type? outputType)
    {
        return new LLMOptions
        {
            ContextWindow = _baseOptions.ContextWindow,
            Temperature = _baseOptions.Temperature,
            TopP = _baseOptions.TopP,
            MaxOutputTokens = _baseOptions.MaxOutputTokens,
            Seed = _baseOptions.Seed,
            StopSequences = _baseOptions.StopSequences,
            FrequencyPenalty = _baseOptions.FrequencyPenalty,
            PresencePenalty = _baseOptions.PresencePenalty,
            ToolCallMode = ToolCallMode.Auto,
            ResponseSchema = outputType?.GetSchemaForType(),
            MaxRetries = _baseOptions.MaxRetries
        };
    }

    private async IAsyncEnumerable<AgentEvent> CoreStreamAsync(
        IContent input,
        Type? outputType,
        List<Message> turnMessages,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var scope = _logger.BeginScope(new[]
        {
            new KeyValuePair<string, object?>("Agent", _config.Name)
        });
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogInformation("Agent invoked. InputLength={InputLength} ContextLimit={ContextLimit}",
                input.ForLlm().Length, _baseOptions.ContextWindow ?? 0);

            var enumerator = CoreStreamInternalAsync(input, outputType, turnMessages, ct)
                .ConfigureAwait(false)
                .GetAsyncEnumerator();

            try
            {
                while (true)
                {
                    AgentEvent evt;
                    try
                    {
                        if (!await enumerator.MoveNextAsync())
                            break;
                        evt = enumerator.Current;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Agent failed. DurationMs={DurationMs}", stopwatch.ElapsedMilliseconds);
                        throw;
                    }

                    yield return evt;
                }

                _logger.LogInformation("Agent completed. DurationMs={DurationMs}", stopwatch.ElapsedMilliseconds);
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }
    }

    private async IAsyncEnumerable<AgentEvent> CoreStreamInternalAsync(
        IContent input,
        Type? outputType,
        List<Message> turnMessages,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var userMessage = new Message(Role.User, input);
        var workingChat = new List<Message> { userMessage };

        var textBuffer = new StringBuilder();
        var reasoningBuffer = new StringBuilder();
        var toolCallsBuffer = new List<ToolCall>();

        // Recall relevant contextual messages (which may include history, summaries, facts, etc.)
        IReadOnlyList<Message> recalled = await _memory.RecallAsync(
            userMessage, 
            _baseOptions.ContextWindow, 
            ct).ConfigureAwait(false);

        var options = BuildLLMOptions(outputType);
        int consecutiveToolSteps = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (consecutiveToolSteps >= _config.MaxToolCalls)
            {
                yield return new TextEvent("You have exceeded the maximum allowed consecutive tool calls. Stop calling tools and respond to the user immediately.");
                break;
            }

            // Assemble complete LLM prompt: System Identity Prompt + Recalled Context + Current Turn Messages
            var messages = new List<Message>();
            if (_config.Instructions != null)
            {
                messages.Add(new Message(Role.System, _config.Instructions));
            }

            messages.AddRange(recalled);
            messages.AddRange(workingChat);
            
            var enumerator = _llm.StreamAsync(messages, options, ct).GetAsyncEnumerator(ct);
            bool hasContextError = false;
            ContextLengthExceededException? capturedEx = null;

            try
            {
                while (true)
                {
                    LLMEvent evt;
                    try
                    {
                        if (!await enumerator.MoveNextAsync())
                            break;
                        evt = enumerator.Current;
                    }
                    catch (ContextLengthExceededException ex)
                    {
                        hasContextError = true;
                        capturedEx = ex;
                        break;
                    }

                    switch (evt)
                    {
                        case TextEvent t:
                            textBuffer.Append(t.Delta);
                            break;

                        case ReasoningEvent r:
                            reasoningBuffer.Append(r.Delta);
                            break;

                        case ToolCallEvent tc:
                            toolCallsBuffer.Add(tc.Call);
                            break;
                    }

                    yield return evt;
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }

            if (hasContextError && capturedEx != null)
            {
                yield return new ErrorEvent(capturedEx);
                yield break;
            }

            if (textBuffer.Length > 0 || reasoningBuffer.Length > 0 || toolCallsBuffer.Count > 0)
            {
                var contents = new List<IContent>();
                if (reasoningBuffer.Length > 0)
                {
                    contents.Add(new Reasoning(reasoningBuffer.ToString()));
                    reasoningBuffer.Clear();
                }
                if (textBuffer.Length > 0)
                {
                    contents.Add(new Text(textBuffer.ToString().Trim()));
                    textBuffer.Clear();
                }
                if (toolCallsBuffer.Count > 0)
                {
                    contents.AddRange(toolCallsBuffer);
                }
                workingChat.Add(new Message(Role.Assistant, contents));
            }

            if (toolCallsBuffer.Count == 0)
            {
                // Turn is complete, record the turn's experience in memory
                await _memory.RememberAsync(workingChat, ct).ConfigureAwait(false);

                turnMessages.AddRange(workingChat);
                break;
            }

            consecutiveToolSteps++;
            var results = await _tooling.ExecuteAsync(toolCallsBuffer, ct).ConfigureAwait(false);
            toolCallsBuffer.Clear();

            foreach (var result in results)
            {
                workingChat.Add(new Message(Role.Tool, result));
                yield return new ToolResultEvent(result);
            }
        }
    }
}
