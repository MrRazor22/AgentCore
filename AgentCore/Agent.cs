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
    private readonly IMemory _memory;
    private readonly ILLMExecutor _llm;
    private readonly IToolExecutor _toolRuntime;
    private readonly ITokenCounter _tokenCounter;
    private readonly LLMOptions _baseOptions;
    private readonly AgentConfig _config;
    private readonly ILogger<LLMAgent> _logger;
    private readonly AgentHooks? _hooks;

    public LLMAgent(
        ILLMExecutor llm,
        IToolExecutor toolRuntime,
        IMemory memory,
        ITokenCounter tokenCounter,
        LLMOptions baseOptions,
        AgentConfig config,
        ILogger<LLMAgent> logger,
        AgentHooks? hooks = null)
    {
        _memory = memory;
        _llm = llm;
        _toolRuntime = toolRuntime;
        _tokenCounter = tokenCounter;
        _baseOptions = baseOptions;
        _config = config;
        _logger = logger;
        _hooks = hooks;
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
        var turnMessages = new List<Message>();

        int inTokens = 0;
        int outTokens = 0;
        int reasoningTokens = 0;

        await foreach (var evt in CoreStreamAsync(input, sessionId, outputType, turnMessages, ct))
        {
            if (evt is LLMMetaEvent meta)
            {
                inTokens += meta.Usage.InputTokens;
                outTokens += meta.Usage.OutputTokens;
                reasoningTokens += meta.Usage.ReasoningTokens;
            }
        }

        var response = new AgentResponse(
            sessionId,
            turnMessages,
            new TokenUsage(inTokens, outTokens, reasoningTokens));

        if (_hooks?.OnAgentEnd != null)
        {
            await _hooks.OnAgentEnd(response);
        }

        return response;
    }

    public IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        IContent input,
        string? sessionId = null,
        CancellationToken ct = default)
    {
        sessionId ??= Guid.NewGuid().ToString("N");
        var turnMessages = new List<Message>();
        return CoreStreamAsync(input, sessionId, null, turnMessages, ct);
    }

    public async Task<AgentResponse> ResumeAsync(string sessionId, string toolCallId, bool approved, CancellationToken ct = default)
    {
        _logger.LogInformation("Resume called: Session={SessionId} ToolCallId={ToolCallId} Approved={Approved}", sessionId, toolCallId, approved);

        // Recall the session history which contains the paused turn
        var history = await _memory.RecallAsync(sessionId, new Message(Role.User, new Text("")), new TokenBudget(0), ct).ConfigureAwait(false);

        // Find the pending tool call
        ToolCall? pendingCall = null;
        foreach (var message in history)
        {
            if (message.Role == Role.Assistant)
            {
                foreach (var content in message.Contents)
                {
                    if (content is ToolCall tc && tc.Id == toolCallId)
                    {
                        pendingCall = tc;
                        break;
                    }
                }
            }
            if (pendingCall != null) break;
        }

        if (pendingCall == null)
        {
            throw new InvalidOperationException($"ToolCall with ID '{toolCallId}' not found in session '{sessionId}'");
        }

        ToolResult result;
        if (approved)
        {
            result = await _toolRuntime.HandleToolCallAsync(pendingCall, ct);
        }
        else
        {
            result = new ToolResult(toolCallId, new Text("Tool execution rejected by user"));
        }

        // Save the tool result to memory directly (append-only)
        var toolResultMsg = new Message(Role.Tool, result);
        await _memory.RememberAsync(sessionId, new[] { toolResultMsg }, ct).ConfigureAwait(false);

        // Resume execution by invoking with an empty text input (continuation of the active session)
        return await InvokeAsyncInternal(new Text(""), sessionId, null, ct);
    }

    private LLMOptions BuildLLMOptions(Type? outputType)
    {
        return new LLMOptions
        {
            Model = _baseOptions.Model,
            ApiKey = _baseOptions.ApiKey,
            BaseUrl = _baseOptions.BaseUrl,
            ContextWindow = _baseOptions.ContextWindow,
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
        List<Message> turnMessages,
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
            var userMessage = new Message(Role.User, input);
            var isContinuation = string.IsNullOrEmpty(input.ForLlm());

            // Recall relevant contextual messages (which may include history, summaries, facts, etc.)
            IReadOnlyList<Message> recalled = [];
            try
            {
                // If it is an empty resume input, we don't query memory with empty string, we query with userMessage representing continuation.
                recalled = await _memory.RecallAsync(
                    sessionId, 
                    userMessage, 
                    new TokenBudget(_baseOptions.ContextWindow ?? 0), 
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Memory recall failed — continuing with empty context.");
            }

            _logger.LogInformation("Agent invoked: Session={SessionId} InputLength={Len} MemoryType={MemType} ContextLimit={CtxLimit}",
                sessionId, input.ForLlm().Length, _memory.GetType().Name, _baseOptions.ContextWindow ?? 0);

            if (_hooks?.OnAgentStart != null)
            {
                await _hooks.OnAgentStart(input, sessionId);
            }

            // Current turn working context: we start with the user message unless it's a resume continuation where input is empty
            var workingChat = new List<Message>();
            if (!isContinuation)
            {
                workingChat.Add(userMessage);
            }

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
                
                // Assemble complete LLM prompt: System Identity Prompt + Recalled Context + Current Turn Messages
                var messages = new List<Message>();
                if (!string.IsNullOrEmpty(_config.SystemPrompt))
                {
                    messages.Add(new Message(Role.System, new Text(_config.SystemPrompt)));
                }

                messages.AddRange(recalled);
                messages.AddRange(workingChat);
                
                _logger.LogDebug("LLM step {Step}: RecalledCount={RecCount} ActiveTurn={TurnMsgs} Tokens≈{Approx}", 
                    consecutiveToolSteps + 1, recalled.Count, workingChat.Count, lastLlmTokens);

                if (_hooks?.OnLLMStart != null)
                {
                    await _hooks.OnLLMStart(new LLMCallContext(messages, options, consecutiveToolSteps));
                }

                var enumerator = _llm.StreamAsync(messages, options, ct).GetAsyncEnumerator(ct);

                try
                {
                    while (await enumerator.MoveNextAsync())
                    {
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
                                
                                if (_hooks?.OnToolStart != null)
                                {
                                    await _hooks.OnToolStart(tc.Call);
                                }
                                
                                runningTools.Add(_toolRuntime.HandleToolCallAsync(tc.Call, ct));
                                break;

                            case LLMMetaEvent meta:
                                if (meta.Usage.IsEmpty)
                                {
                                    lastLlmTokens = await _tokenCounter.CountAsync(workingChat, ct).ConfigureAwait(false) + meta.ToolSchemaTokens;
                                    _logger.LogDebug("LLM Provider did not report tokens. Counted {TokenCount} natively (including {ToolSchemaTokens} tool schemas).", lastLlmTokens, meta.ToolSchemaTokens);
                                }
                                else
                                {
                                    lastLlmTokens = meta.Usage.InputTokens + meta.Usage.OutputTokens;
                                }

                                if (_hooks?.OnLLMEnd != null)
                                {
                                    await _hooks.OnLLMEnd(new LLMCallContext(messages, options, consecutiveToolSteps), meta);
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
                }

                if (runningTools.Count == 0)
                {
                    _logger.LogDebug("Agent completed: Steps={Steps} TotalToolCalls={Count}", consecutiveToolSteps, totalToolCalls);

                    // Turn is complete, record the turn's experience in memory
                    try
                    {
                        await _memory.RememberAsync(sessionId, workingChat, ct).ConfigureAwait(false);
                        _logger.LogDebug("Memory saved: Success MessageCount={Count}", workingChat.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Memory saved: Failed MessageCount={Count}", workingChat.Count);
                    }

                    turnMessages.AddRange(workingChat);
                    break;
                }

                consecutiveToolSteps++;
                totalToolCalls += runningTools.Count;
                var toolStartTime = DateTime.UtcNow;
                var results = await Task.WhenAll(runningTools);
                var toolDuration = (DateTime.UtcNow - toolStartTime).TotalMilliseconds;

                var pendingApprovals = new List<string>();
                foreach (var result in results)
                {
                    var toolName = pendingToolCalls.TryGetValue(result.CallId, out var tc) ? tc.Name : "unknown";
                    var resultLength = result.ForLlm()?.Length ?? 0;
                    
                    if (result.Result is ToolExecutionException tex && tex.Message.Contains("requires approval"))
                    {
                        pendingApprovals.Add(result.CallId);
                        _logger.LogInformation("Tool pending approval: {ToolName} CallId={CallId}", toolName, result.CallId);
                    }
                    
                    _logger.LogDebug("Tool result: {ToolName} Duration={Ms}ms ResultLength={Len}", toolName, toolDuration, resultLength);
                    
                    if (_hooks?.OnToolEnd != null)
                    {
                        await _hooks.OnToolEnd(pendingToolCalls[result.CallId], result);
                    }
                    
                    workingChat.Add(new Message(Role.Tool, result));
                    yield return new AgentToolResultEvent(result);
                }

                if (pendingApprovals.Count > 0)
                {
                    _logger.LogInformation("Pausing execution: {Count} tools pending approval", pendingApprovals.Count);
                    
                    // Save active turn context up to this point so it can be resumed later
                    try
                    {
                        await _memory.RememberAsync(sessionId, workingChat, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Memory save on pause: Failed");
                    }

                    turnMessages.AddRange(workingChat);
                    break;
                }

                pendingToolCalls.Clear();
            }
        }
    }
}
