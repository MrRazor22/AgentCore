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

    public async Task<T?> InvokeAsync<T>(IContent input, Type? outputType, CancellationToken ct = default)
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

        IContent response = lastAssistantMsg?.Contents.OfType<Text>().LastOrDefault()
            ?? (lastAssistantMsg?.Contents.LastOrDefault() ?? new Text(""));
        
        var json = response.ForLlm();
        
        if (string.IsNullOrWhiteSpace(json)) return default;
        return System.Text.Json.JsonSerializer.Deserialize<T>(json);
    }

    public IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        IContent input,
        CancellationToken ct = default)
    {
        var turnMessages = new List<Message>();
        return CoreStreamAsync(input, null, turnMessages, ct);
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
        var currentTurnChat = new List<Message> { userMessage };

        // Recall relevant contextual messages (which may include history, summaries, facts, etc.)
        IReadOnlyList<Message> recalledChat = await _memory.RecallAsync(
            userMessage, 
            _baseOptions.ContextWindow, 
            ct).ConfigureAwait(false);

        var responseSchema = outputType?.GetSchemaForType();
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

            messages.AddRange(recalledChat);
            messages.AddRange(currentTurnChat);

            Message? assistantMessage = null;

            var enumerator = _llm.StreamAsync(messages, _baseOptions, responseSchema, ct).GetAsyncEnumerator(ct);
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

                    if (evt is AssistantMessageEvent am)
                    {
                        assistantMessage = am.Message;
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
                break;
            }

            if (assistantMessage == null)
            {
                break;
            }

            currentTurnChat.Add(assistantMessage);

            var toolCalls = assistantMessage.Contents.OfType<ToolCall>().ToList();
            if (toolCalls.Count == 0)
            {
                // Turn is complete, record the turn's experience in memory
                await _memory.RememberAsync(currentTurnChat, ct).ConfigureAwait(false);

                turnMessages.AddRange(currentTurnChat);
                break;
            }

            consecutiveToolSteps++;
            var toolMessages = await _tooling.ExecuteAsync(toolCalls, ct).ConfigureAwait(false);
            currentTurnChat.AddRange(toolMessages);

            foreach (var message in toolMessages)
            {
                var result = message.Contents.OfType<ToolResult>().Single();
                yield return new ToolResultEvent(result);
            }
        }
    }
}
