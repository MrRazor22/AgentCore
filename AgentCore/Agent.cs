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
            var isNewSession = chat.Count == 0;
            _logger.LogInformation("Agent invoked: Session={SessionId} InputLength={Len} NewSession={IsNew}", sessionId, input.ForLlm().Length, isNewSession);
            
            var userMessage = new Message(Role.User, input);
            chat.Add(userMessage);
            await _memory.UpdateAsync(sessionId, chat);

            var workingChat = new List<Message>(chat);
            var pendingToolCalls = new Dictionary<string, ToolCall>();
            var textBuffer = new StringBuilder();
            var reasoningBuffer = new StringBuilder();
            var toolCallsBuffer = new List<ToolCall>();

            var options = BuildLLMOptions(outputType);
            int consecutiveToolSteps = 0;
            int totalToolCalls = 0;
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
                var messages = (IReadOnlyList<Message>)workingChat.GetActiveWindow();
                _logger.LogDebug("LLM step {Step}: Messages={Count} ContextTokens≈{Approx}", consecutiveToolSteps + 1, messages.Count, lastLlmTokens);

                var enumerator = _llm.StreamAsync(messages, options, ct).GetAsyncEnumerator(ct);
                bool limitsExceeded = false;

                try
                {
                    while (true)
                    {
                        bool hasNext = false;
                        try
                        {
                            hasNext = await enumerator.MoveNextAsync();
                        }
                        catch (ContextLengthExceededException)
                        {
                            limitsExceeded = true;
                            break;
                        }

                        if (!hasNext) break;

                        var evt = enumerator.Current;
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
                                pendingToolCalls[tc.Call.Id] = tc.Call;
                                _logger.LogInformation("Tool called: {ToolName}", tc.Call.Name);
                                runningTools.Add(_toolRuntime.HandleToolCallAsync(tc.Call, ct));
                                break;

                            case LLMMetaEvent meta:
                                if (meta.Usage.IsEmpty)
                                {
                                    lastLlmTokens = await _tokenCounter.CountAsync(workingChat.GetActiveWindow(), ct).ConfigureAwait(false) + meta.ToolSchemaTokens;
                                    _logger.LogDebug("LLM Provider did not report tokens. Counted {TokenCount} natively (including {ToolSchemaTokens} tool schemas).", lastLlmTokens, meta.ToolSchemaTokens);
                                }
                                else
                                {
                                    lastLlmTokens = meta.Usage.InputTokens + meta.Usage.OutputTokens;
                                }
                                break;
                        }

                        yield return evt;
                    }
                }
                finally
                {
                    await enumerator.DisposeAsync();
                }

                if (limitsExceeded)
                {
                    _logger.LogWarning("Context limit exceeded. Forcing proactive summarization and retrying.");
                    
                    int currentTokensCount = await _tokenCounter.CountAsync(messages, ct);
                    int limit = options.ContextLength ?? (int)(currentTokensCount * 0.75);
                    
                    workingChat = await _ctxManager.ReduceAsync(workingChat, currentTokensCount, new LLMOptions { Model = options.Model, ContextLength = limit }, ct);
                    
                    chat.Clear();
                    chat.AddRange(workingChat);
                    
                    textBuffer.Clear();
                    reasoningBuffer.Clear();
                    toolCallsBuffer.Clear();
                    pendingToolCalls.Clear();
                    runningTools.Clear();
                    
                    continue;
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
                        toolCallsBuffer.Clear();
                    }
                    workingChat.Add(new Message(Role.Assistant, contents));
                    chat.Add(new Message(Role.Assistant, contents));
                }

                if (runningTools.Count == 0)
                {
                    _logger.LogDebug("Agent completed: Steps={Steps} TotalToolCalls={Count}", consecutiveToolSteps, totalToolCalls);
                    await _memory.UpdateAsync(sessionId, chat);
                    break;
                }

                consecutiveToolSteps++;
                totalToolCalls += runningTools.Count;
                var toolStartTime = DateTime.UtcNow;
                var results = await Task.WhenAll(runningTools);
                var toolDuration = (DateTime.UtcNow - toolStartTime).TotalMilliseconds;

                foreach (var result in results)
                {
                    var resultLength = result.ForLlm()?.Length ?? 0;
                    var toolName = pendingToolCalls.TryGetValue(result.CallId, out var tc) ? tc.Name : "unknown";
                    _logger.LogDebug("Tool result: {ToolName} Duration={Ms}ms ResultLength={Len}", toolName, toolDuration, resultLength);
                    workingChat.Add(new Message(Role.Tool, result));
                    chat.Add(new Message(Role.Tool, result));
                    yield return new AgentToolResultEvent(result);
                }

                pendingToolCalls.Clear();


                await _memory.UpdateAsync(sessionId, chat);
            }
        }
    }
}
