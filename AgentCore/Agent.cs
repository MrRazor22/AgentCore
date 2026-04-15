using AgentCore.Conversation;
using AgentCore.Diagnostics;
using AgentCore.Json;
using AgentCore.LLM;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;

namespace AgentCore;

public interface IAgent
{
    Task<AgentResponse> InvokeAsync(IContent input, string? sessionId = null, CancellationToken ct = default);
    Task<T?> InvokeAsync<T>(IContent input, string? sessionId = null, CancellationToken ct = default);
    IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(IContent input, string? sessionId = null, CancellationToken ct = default);
}

public sealed class LLMAgent : IAgent
{
    private readonly IChat _chatStore;
    private readonly IAgentMemory? _memory;
    private readonly ILLMExecutor _llm;
    private readonly IToolExecutor _toolRuntime;
    private readonly IContextCompactor _ctxCompactor;
    private readonly List<Scratchpad> _blocks;
    private readonly ITokenCounter _tokenCounter;
    private readonly LLMOptions _baseOptions;
    private readonly AgentConfig _config;
    private readonly ILogger<LLMAgent> _logger;

    public LLMAgent(
        IChat chatStore,
        ILLMExecutor llm,
        IToolExecutor toolRuntime,
        IContextCompactor contextCompactor,
        IEnumerable<Scratchpad> blocks,
        ITokenCounter tokenCounter,
        LLMOptions baseOptions,
        AgentConfig config,
        ILogger<LLMAgent> logger,
        IAgentMemory? memory = null)
    {
        _chatStore = chatStore;
        _memory = memory;
        _llm = llm;
        _toolRuntime = toolRuntime;
        _ctxCompactor = contextCompactor;
        _blocks = blocks.ToList();
        _tokenCounter = tokenCounter;
        _baseOptions = baseOptions;
        _config = config;
        _logger = logger;
    }

    public static AgentBuilder Create(string name = "agent")
        => new AgentBuilder().WithName(name);

    public Task<AgentResponse> InvokeAsync(IContent input, string? sessionId = null, CancellationToken ct = default)
        => InvokeAsyncInternal(input, sessionId, null, ct);

    public async Task<T?> InvokeAsync<T>(IContent input, string? sessionId = null, CancellationToken ct = default)
    {
        var response = await InvokeAsyncInternal(input, sessionId, typeof(T), ct);
        var json = response.Text;
        
        if (string.IsNullOrWhiteSpace(json)) return default;
        return System.Text.Json.JsonSerializer.Deserialize<T>(json);
    }

    private async Task<AgentResponse> InvokeAsyncInternal(
        IContent input, 
        string? sessionId, 
        Type? outputType, 
        CancellationToken ct)
    {
        sessionId ??= Guid.NewGuid().ToString("N");
        
        var chatBefore = await _chatStore.RecallAsync(sessionId);
        int startIndex = chatBefore.Count;

        int inTokens = 0;
        int outTokens = 0;
        int reasoningTokens = 0;

        await foreach (var evt in CoreStreamAsync(input, sessionId, outputType, ct))
        {
            if (evt is LLMMetaEvent meta)
            {
                inTokens += meta.Usage.InputTokens;
                outTokens += meta.Usage.OutputTokens;
                reasoningTokens += meta.Usage.ReasoningTokens;
            }
        }

        var chatAfter = await _chatStore.RecallAsync(sessionId);
        var turnMessages = chatAfter.Skip(startIndex).ToList();

        return new AgentResponse(
            sessionId,
            turnMessages,
            new TokenUsage(inTokens, outTokens, reasoningTokens));
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
            var chat = await _chatStore.RecallAsync(sessionId);
            var isNewSession = chat.Count == 0;
            _logger.LogInformation("Agent invoked: Session={SessionId} InputLength={Len} NewSession={IsNew}", sessionId, input.ForLlm().Length, isNewSession);
            
            var userMessage = new Message(Role.User, input);
            chat.Add(userMessage);
            await _chatStore.UpdateAsync(sessionId, chat);

            // Recall cognitive memory before first LLM step
            List<Message> memoryMessages = [];
            if (_memory != null)
            {
                try
                {
                    var recalled = await _memory.RecallAsync(input.ForLlm(), ct);
                    if (recalled.Count > 0)
                        memoryMessages = [new Message(Role.System, recalled)];
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Memory recall failed — continuing without memory context.");
                }
            }

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
                
                // Assemble context (Render blocks followed by cognitive memory & history)
                var messages = new List<Message>();

                // Group scratchpad blocks by role to avoid consecutive same-role messages
                var blocksByRole = _blocks
                    .Where(b => !string.IsNullOrEmpty(b.Value))
                    .GroupBy(b => b.Role);

                foreach (var group in blocksByRole)
                {
                    var combinedContent = string.Join("\n\n", group.Select(b => b.ToLlmString()));
                    messages.Add(new Message(group.Key, new Text(combinedContent)));
                }

                if (memoryMessages.Count > 0)
                    messages.AddRange(memoryMessages);

                var activeWindow = workingChat.GetActiveWindow();
                messages.AddRange(activeWindow);
                
                _logger.LogDebug("LLM step {Step}: Blocks={BlockCount} History={HistMsgs} Tokens≈{Approx}", 
                    consecutiveToolSteps + 1, _blocks.Count, activeWindow.Count, lastLlmTokens);

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
                    
                    workingChat = await _ctxCompactor.ReduceAsync(workingChat, currentTokensCount, new LLMOptions { Model = options.Model, ContextLength = limit }, ct);
                    
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
                    if (options.ContextLength.HasValue)
                    {
                        _logger.LogDebug("Context reduction requested: {Tokens}/{Limit}", lastLlmTokens, options.ContextLength.Value);
                        chat = await _ctxCompactor.ReduceAsync(chat, lastLlmTokens, options, ct).ConfigureAwait(false);
                        workingChat.Clear();
                        workingChat.AddRange(chat);
                    }
                    _logger.LogDebug("Agent completed: Steps={Steps} TotalToolCalls={Count}", consecutiveToolSteps, totalToolCalls);
                    await _chatStore.UpdateAsync(sessionId, chat);

                    // Fire-and-forget: retain knowledge from this turn
                    if (_memory != null)
                    {
                        var turnSnapshot = new List<Message>(chat);
                        _ = Task.Run(async () =>
                        {
                            try { await _memory.RetainAsync(turnSnapshot, CancellationToken.None); }
                            catch { /* non-fatal */ }
                        }, CancellationToken.None);
                    }
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

                if (options.ContextLength.HasValue)
                {
                    _logger.LogDebug("Context reduction requested: {Tokens}/{Limit}", lastLlmTokens, options.ContextLength.Value);
                    chat = await _ctxCompactor.ReduceAsync(chat, lastLlmTokens, options, ct).ConfigureAwait(false);
                    workingChat.Clear();
                    workingChat.AddRange(chat);
                }

                await _chatStore.UpdateAsync(sessionId, chat);
            }
        }
    }
}
