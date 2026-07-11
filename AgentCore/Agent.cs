using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.LLM.Exceptions;
using AgentCore.Memory;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;

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
    private readonly LLMOptions _baseOptions;
    private readonly AgentConfig _config;
    private readonly ILogger<LLMAgent> _logger; 

    public LLMAgent(
        ILLM llm,
        ITooling tooling,
        IMemory memory,
        LLMOptions baseOptions,
        AgentConfig config,
        ILogger<LLMAgent>? logger = null)
    {
        _memory = memory;
        _llm = llm;
        _tooling = tooling;
        _baseOptions = baseOptions;
        _config = config;
        _logger = logger ?? NullLogger<LLMAgent>.Instance;
    } 

    public async Task<T?> InvokeAsync<T>(IContent input, CancellationToken ct = default)
    {
        var turnMessages = new List<Message>();

        await foreach (var evt in CoreStreamAsync(input, turnMessages, ct))
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
        
        var rawText = response.ForLlm();
        
        if (typeof(T) == typeof(string))
        {
            return (T?)(object)rawText;
        }

        if (string.IsNullOrWhiteSpace(rawText)) return default;
        return System.Text.Json.JsonSerializer.Deserialize<T>(rawText);
    }

    public IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        IContent input,
        CancellationToken ct = default)
    {
        var turnMessages = new List<Message>();
        return CoreStreamAsync(input, turnMessages, ct);
    }

    private async IAsyncEnumerable<AgentEvent> CoreStreamAsync(
        IContent input,
        List<Message> turnMessages,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var scope = _logger.BeginScope(new[]
        {
            new KeyValuePair<string, object?>("Agent", _config.Name)
        });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Agent invoked. InputLength={InputLength} ContextLimit={ContextLimit}",
            input.ForLlm().Length, _baseOptions.ContextWindow ?? 0);

        var userMessage = new Message(Role.User, input);
        var currentTurnChat = new List<Message> { userMessage };

        IReadOnlyList<Message> recalledChat;
        try
        {
            recalledChat = await _memory.RecallAsync(
                userMessage, 
                _baseOptions.ContextWindow, 
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent failed during memory recall. DurationMs={DurationMs}", stopwatch.ElapsedMilliseconds);
            throw;
        }

        var responseSchema = _config.OutputSchema;
        int consecutiveToolSteps = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (consecutiveToolSteps >= _config.MaxToolCalls)
            {
                yield return new TextEvent("You have exceeded the maximum allowed consecutive tool calls. Stop calling tools and respond to the user immediately.");
                break;
            }

            var messages = new List<Message>();
            if (_config.Instructions != null)
            {
                messages.Add(new Message(Role.System, _config.Instructions));
            }

            messages.AddRange(recalledChat);
            messages.AddRange(currentTurnChat);

            Message? assistantMessage = null;
            var textBuffer = new System.Text.StringBuilder();
            var reasoningBuffer = new System.Text.StringBuilder();
            var toolCallsBuffer = new List<ToolCall>();

            var enumerator = _llm.StreamAsync(messages, _baseOptions, responseSchema, ct).GetAsyncEnumerator(ct);
            bool hasContextError = false;
            ContextLengthExceededException? capturedEx = null;
            Exception? otherEx = null;

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
                    catch (Exception ex)
                    {
                        otherEx = ex;
                        break;
                    }

                    switch (evt)
                    {
                        case TextEvent te:    textBuffer.Append(te.Delta); break;
                        case ReasoningEvent re: reasoningBuffer.Append(re.Delta); break;
                        case ToolCallEvent tc: toolCallsBuffer.Add(tc.Call); break;
                    }

                    yield return evt;
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }

            // Reconstruct the assistant message from what we buffered
            var contents = new List<IContent>();
            if (reasoningBuffer.Length > 0) contents.Add(new Reasoning(reasoningBuffer.ToString()));
            if (textBuffer.Length > 0)      contents.Add(new Text(textBuffer.ToString().Trim()));
            if (toolCallsBuffer.Count > 0)  contents.AddRange(toolCallsBuffer);
            if (contents.Count > 0)
                assistantMessage = new Message(Role.Assistant, contents);

            if (otherEx != null)
            {
                _logger.LogError(otherEx, "Agent failed during LLM stream. DurationMs={DurationMs}", stopwatch.ElapsedMilliseconds);
                throw otherEx;
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
                try
                {
                    await _memory.RememberAsync(currentTurnChat, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Agent failed during memory remember. DurationMs={DurationMs}", stopwatch.ElapsedMilliseconds);
                    throw;
                }

                turnMessages.AddRange(currentTurnChat);
                break;
            }

            consecutiveToolSteps++;

            IReadOnlyList<Message> toolMessages;
            try
            {
                toolMessages = await _tooling.ExecuteAsync(toolCalls, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent failed during tool execution. DurationMs={DurationMs}", stopwatch.ElapsedMilliseconds);
                throw;
            }

            currentTurnChat.AddRange(toolMessages);

            foreach (var message in toolMessages)
            {
                var result = message.Contents.OfType<ToolResult>().Single();
                yield return new ToolResultEvent(result);
            }
        }

        _logger.LogInformation("Agent completed. DurationMs={DurationMs}", stopwatch.ElapsedMilliseconds);
    }
}


