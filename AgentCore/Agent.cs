using AgentCore.Conversation;
using AgentCore.Diagnostics;
using AgentCore.Json;
using AgentCore.LLM;
using AgentCore.Runtime;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;

namespace AgentCore;

public interface IAgent
{
    Task<string> InvokeAsync(IContent input, string? sessionId = null, CancellationToken ct = default);
    Task<T?> InvokeAsync<T>(IContent input, string? sessionId = null, CancellationToken ct = default);
    IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(IContent input, string? sessionId = null, CancellationToken ct = default);
}

public sealed class LLMAgent : IAgent
{
    private readonly IAgentMemory _memory;
    private readonly ILLMExecutor _llm;
    private readonly IToolExecutor _toolRuntime;
    private readonly IContextManager _ctxManager;
    private readonly ITokenCounter _tokenCounter;
    private readonly LLMOptions _baseOptions;
    private readonly AgentConfig _config;
    private readonly ILogger<LLMAgent> _logger;

    public LLMAgent(
        IAgentMemory memory,
        ILLMExecutor llm,
        IToolExecutor toolRuntime,
        IContextManager contextManager,
        ITokenCounter tokenCounter,
        LLMOptions baseOptions,
        AgentConfig config,
        ILogger<LLMAgent> logger)
    {
        _memory = memory;
        _llm = llm;
        _toolRuntime = toolRuntime;
        _ctxManager = contextManager;
        _tokenCounter = tokenCounter;
        _baseOptions = baseOptions;
        _config = config;
        _logger = logger;
    }

    public static AgentBuilder Create(string name = "agent")
        => new AgentBuilder().WithName(name);

    public async Task<string> InvokeAsync(IContent input, string? sessionId = null, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var evt in CoreStreamAsync(input, sessionId, null, ct))
        {
            if (evt is TextEvent text)
                sb.Append(text.Delta);
        }
        return sb.ToString();
    }

    public async Task<T?> InvokeAsync<T>(IContent input, string? sessionId = null, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var evt in CoreStreamAsync(input, sessionId, typeof(T), ct))
        {
            if (evt is TextEvent text)
                sb.Append(text.Delta);
        }

        var json = sb.ToString();
        if (string.IsNullOrWhiteSpace(json)) return default;
        return System.Text.Json.JsonSerializer.Deserialize<T>(json);
    }

    public IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        IContent input,
        string? sessionId = null,
        CancellationToken ct = default)
    {
        sessionId ??= Guid.NewGuid().ToString("N");
        return CoreStreamAsync(input, sessionId, null, ct);
    }

    private LLMOptions BuildLLMOptions(Type? outputType)
    {
        return new LLMOptions
        {
            Model = _baseOptions.Model,
            ApiKey = _baseOptions.ApiKey,
            BaseUrl = _baseOptions.BaseUrl,
            ContextLength = _baseOptions.ContextLength,
            Temperature = _baseOptions.Temperature,
            TopP = _baseOptions.TopP,
            MaxOutputTokens = _baseOptions.MaxOutputTokens,
            Seed = _baseOptions.Seed,
            StopSequences = _baseOptions.StopSequences,
            FrequencyPenalty = _baseOptions.FrequencyPenalty,
            PresencePenalty = _baseOptions.PresencePenalty,
            ToolCallMode = ToolCallMode.Auto,
            ResponseSchema = outputType?.GetSchemaForType()
        };
    }

    private async IAsyncEnumerable<AgentEvent> CoreStreamAsync(
        IContent input,
        string sessionId,
        Type? outputType,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var activity = AgentDiagnosticSource.Source.StartActivity("AgentCore.Invoke");
        activity?.SetTag("agent.name", _config.Name);
        activity?.SetTag("agent.session", sessionId);

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["Agent"] = _config.Name,
            ["Session"] = sessionId
        }))
        {
            var chat = await _memory.RecallAsync(sessionId);
            var userMessage = new Message(Role.User, input);
            chat.Add(userMessage);

            var pendingCalls = new Dictionary<string, Message>();
            var textBuffer = new StringBuilder();
            var reasoningBuffer = new StringBuilder();

            var options = BuildLLMOptions(outputType);
            int consecutiveToolSteps = 0;
            int lastLlmTokens = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (consecutiveToolSteps >= _config.MaxToolCalls)
                {
                    yield return new TextEvent("You have exceeded the maximum allowed consecutive tool calls. Stop calling tools and respond to the user immediately.");
                    break;
                }

                var runningTools = new List<Task<ToolResult>>();
                var messages = (IReadOnlyList<Message>)chat.GetActiveWindow();

                await foreach (var evt in _llm.StreamAsync(messages, options, ct))
                {
                    // Side-effects first, then pass event through unchanged — no re-boxing needed
                    // since LLMEvent : AgentEvent directly.
                    switch (evt)
                    {
                        case TextEvent t:
                            textBuffer.Append(t.Delta);
                            break;

                        case ReasoningEvent r:
                            reasoningBuffer.Append(r.Delta);
                            break;

                        case ToolCallEvent tc:
                            if (textBuffer.Length > 0)
                            {
                                chat.Add(new Message(Role.Assistant, new Text(textBuffer.ToString())));
                                textBuffer.Clear();
                            }
                            reasoningBuffer.Clear();
                            var callMsg = new Message(Role.Assistant, tc.Call);
                            pendingCalls[tc.Call.Id] = callMsg;
                            chat.Add(callMsg);
                            _logger.LogInformation("Tool called: {ToolName}", tc.Call.Name);
                            runningTools.Add(_toolRuntime.HandleToolCallAsync(tc.Call, ct));
                            break;

                        case LLMMetaEvent meta:
                            lastLlmTokens = meta.Usage.InputTokens + meta.Usage.OutputTokens;
                            break;
                    }

                    yield return evt; // LLMEvent IS an AgentEvent — pass straight through
                }

                if (textBuffer.Length > 0)
                {
                    chat.Add(new Message(Role.Assistant, new Text(textBuffer.ToString().Trim())));
                    textBuffer.Clear();
                }

                if (runningTools.Count == 0)
                {
                    if (options.ContextLength.HasValue)
                    {
                        if (lastLlmTokens == 0)
                        {
                            lastLlmTokens = await _tokenCounter.CountAsync(chat.GetActiveWindow(), ct).ConfigureAwait(false);
                            _logger.LogDebug("LLM Provider did not report tokens. Counted {TokenCount} natively.", lastLlmTokens);
                        }
                        chat = await _ctxManager.ReduceAsync(chat, lastLlmTokens, options, ct).ConfigureAwait(false);
                    }
                    await _memory.UpdateAsync(sessionId, chat);
                    break;
                }

                consecutiveToolSteps++;
                var results = await Task.WhenAll(runningTools);

                foreach (var result in results)
                {
                    chat.Add(new Message(Role.Tool, result));
                    yield return new AgentToolResultEvent(result);
                }

                pendingCalls.Clear();

                if (options.ContextLength.HasValue)
                {
                    if (lastLlmTokens == 0)
                    {
                        lastLlmTokens = await _tokenCounter.CountAsync(chat.GetActiveWindow(), ct).ConfigureAwait(false);
                        _logger.LogDebug("LLM Provider did not report tokens. Counted {TokenCount} natively.", lastLlmTokens);
                    }
                    chat = await _ctxManager.ReduceAsync(chat, lastLlmTokens, options, ct).ConfigureAwait(false);
                }

                await _memory.UpdateAsync(sessionId, chat);
            }
        }
    }
}
