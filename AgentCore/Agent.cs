using AgentCore.Conversation;
using AgentCore.Json;
using AgentCore.LLM;
using AgentCore.Memory;
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
    Task<AgentResponse> ResumeAsync(string sessionId, string toolCallId, bool approved, CancellationToken ct = default);
}

public sealed class LLMAgent : IAgent
{
    private readonly IChat _chatStore;
    private readonly IAgentMemory _memory;
    private readonly ILLMExecutor _llm;
    private readonly IToolExecutor _toolRuntime;
    private readonly IContextCompactor _ctxCompactor;
    private readonly ITokenCounter _tokenCounter;
    private readonly LLMOptions _baseOptions;
    private readonly AgentConfig _config;
    private readonly ILogger<LLMAgent> _logger;

    public LLMAgent(
        IChat chatStore,
        ILLMExecutor llm,
        IToolExecutor toolRuntime,
        IContextCompactor contextCompactor,
        IAgentMemory memory,
        ITokenCounter tokenCounter,
        LLMOptions baseOptions,
        AgentConfig config,
        ILogger<LLMAgent> logger)
    {
        _chatStore = chatStore;
        _memory = memory;
        _llm = llm;
        _toolRuntime = toolRuntime;
        _ctxCompactor = contextCompactor;
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

    public async Task<AgentResponse> ResumeAsync(string sessionId, string toolCallId, bool approved, CancellationToken ct = default)
    {
        _logger.LogInformation("Resume called: Session={SessionId} ToolCallId={ToolCallId} Approved={Approved}", sessionId, toolCallId, approved);

        var chat = await _chatStore.RecallAsync(sessionId);
        var updated = false;

        // Find and update the ToolCall with matching Id
        for (int i = 0; i < chat.Count; i++)
        {
            var message = chat[i];
            if (message.Role == Role.Assistant)
            {
                var updatedContents = new List<IContent>();
                foreach (var content in message.Contents)
                {
                    if (content is ToolCall tc && tc.Id == toolCallId)
                    {
                        var updatedCall = tc with { IsApproved = approved };
                        updatedContents.Add(updatedCall);
                        updated = true;
                        _logger.LogInformation("Updated ToolCall approval: {ToolCallId} IsApproved={IsApproved}", toolCallId, updatedCall.IsApproved);
                    }
                    else
                    {
                        updatedContents.Add(content);
                    }
                }

                if (updated)
                {
                    chat[i] = new Message(Role.Assistant, updatedContents);
                    break;
                }
            }
        }

        if (!updated)
        {
            _logger.LogWarning("ToolCall not found: {ToolCallId}", toolCallId);
            throw new InvalidOperationException($"ToolCall with ID '{toolCallId}' not found in session '{sessionId}'");
        }

        await _chatStore.UpdateAsync(sessionId, chat);

        // Resume execution from where we left off
        // We'll re-run the agent with the updated conversation history
        // The ToolExecutor will now see the Approved/Rejected status and execute/skip accordingly
        return await InvokeAsyncInternal(new Text(""), sessionId, null, ct);
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
            _logger.LogInformation("Agent invoked: Session={SessionId} InputLength={Len} NewSession={IsNew} MemoryType={MemType} ContextLimit={CtxLimit}",
                sessionId, input.ForLlm().Length, isNewSession, _memory?.GetType().Name ?? "None", _baseOptions.ContextLength ?? 0);
            
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
                    {
                        memoryMessages = [new Message(Role.System, recalled)];
                        var recalledText = string.Join("\n\n", recalled.Select(c => c.ForLlm()));
                        _logger.LogDebug("Memory recall result: ContentCount={Count} ContentPreview={Preview}",
                            recalled.Count, recalledText.Length > 200 ? recalledText[..200] + "..." : recalledText);
                    }
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
                
                // Assemble context using IAgentMemory.RecallAsync
                var messages = new List<Message>();

                // Recall memories for injection (CoreMemory returns blocks, MemoryEngine returns semantic matches)
                var memoryContents = await _memory.RecallAsync(textBuffer.ToString(), ct);
                if (memoryContents.Count > 0)
                {
                    var totalLength = memoryContents.Sum(c => c.ForLlm()?.Length ?? 0);
                    _logger.LogDebug("Memory injection: ContentCount={Count} TotalLength={Len}", memoryContents.Count, totalLength);
                    messages.Add(new Message(Role.System, new Text(string.Join("\n\n", memoryContents.Select(c => c.ToString())))));
                }

                var activeWindow = workingChat.GetActiveWindow();
                messages.AddRange(activeWindow);
                
                _logger.LogDebug("LLM step {Step}: MemoryCount={MemCount} History={HistMsgs} Tokens≈{Approx}", 
                    consecutiveToolSteps + 1, memoryContents.Count, activeWindow.Count, lastLlmTokens);

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
                                var argsJson = tc.Call.Arguments?.ToString() ?? "{}";
                                _logger.LogInformation("Tool called: {ToolName} Args={Args}", tc.Call.Name, argsJson.Length > 200 ? argsJson[..200] + "..." : argsJson);
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
                    int currentTokensCount = await _tokenCounter.CountAsync(messages, ct);
                    int limit = options.ContextLength ?? (int)(currentTokensCount * 0.75);
                    _logger.LogWarning("Context limit exceeded: CurrentTokens={Current} Limit={Limit} Forcing proactive summarization and retrying.", currentTokensCount, limit);
                    
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
                        workingChat = await _ctxCompactor.ReduceAsync(workingChat, lastLlmTokens, options, ct).ConfigureAwait(false);
                    }
                    _logger.LogDebug("Agent completed: Steps={Steps} TotalToolCalls={Count}", consecutiveToolSteps, totalToolCalls);
                    await _chatStore.UpdateAsync(sessionId, chat);

                    // Fire-and-forget: retain knowledge from this turn
                    if (_memory != null)
                    {
                        var turnSnapshot = new List<Message>(chat);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _memory.RetainAsync(turnSnapshot, CancellationToken.None);
                                _logger.LogDebug("Memory retain: Success MessageCount={Count}", turnSnapshot.Count);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Memory retain: Failed MessageCount={Count}", turnSnapshot.Count);
                            }
                        }, CancellationToken.None);
                    }
                    break;
                }

                consecutiveToolSteps++;
                totalToolCalls += runningTools.Count;
                var toolStartTime = DateTime.UtcNow;
                var results = await Task.WhenAll(runningTools);
                var toolDuration = (DateTime.UtcNow - toolStartTime).TotalMilliseconds;

                // Check if any tools are pending approval
                var pendingApprovals = new List<string>();
                foreach (var result in results)
                {
                    var toolName = pendingToolCalls.TryGetValue(result.CallId, out var tc) ? tc.Name : "unknown";
                    var resultLength = result.ForLlm()?.Length ?? 0;
                    
                    // Check if this is a pending approval error
                    if (result.Result is ToolExecutionException tex && tex.Message.Contains("requires approval"))
                    {
                        pendingApprovals.Add(result.CallId);
                        _logger.LogInformation("Tool pending approval: {ToolName} CallId={CallId}", toolName, result.CallId);
                    }
                    
                    _logger.LogDebug("Tool result: {ToolName} Duration={Ms}ms ResultLength={Len}", toolName, toolDuration, resultLength);
                    workingChat.Add(new Message(Role.Tool, result));
                    chat.Add(new Message(Role.Tool, result));
                    yield return new AgentToolResultEvent(result);
                }

                // If there are pending approvals, pause execution
                if (pendingApprovals.Count > 0)
                {
                    _logger.LogInformation("Pausing execution: {Count} tools pending approval", pendingApprovals.Count);
                    await _chatStore.UpdateAsync(sessionId, chat);
                    break;
                }

                pendingToolCalls.Clear();

                if (options.ContextLength.HasValue)
                {
                    _logger.LogDebug("Context reduction requested: {Tokens}/{Limit}", lastLlmTokens, options.ContextLength.Value);
                    workingChat = await _ctxCompactor.ReduceAsync(workingChat, lastLlmTokens, options, ct).ConfigureAwait(false);
                }

                await _chatStore.UpdateAsync(sessionId, chat);
            }
        }
    }
}
