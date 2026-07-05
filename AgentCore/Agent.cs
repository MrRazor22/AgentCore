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
    Task<AgentResponse> InvokeAsync(IContent input, CancellationToken ct = default);
    Task<T?> InvokeAsync<T>(IContent input, CancellationToken ct = default);
    IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(IContent input, CancellationToken ct = default);
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

    public LLMAgent(
        ILLMExecutor llm,
        IToolExecutor toolRuntime,
        IMemory memory,
        ITokenCounter tokenCounter,
        LLMOptions baseOptions,
        AgentConfig config,
        ILogger<LLMAgent> logger)
    {
        _memory = memory;
        _llm = llm;
        _toolRuntime = toolRuntime;
        _tokenCounter = tokenCounter;
        _baseOptions = baseOptions;
        _config = config;
        _logger = logger;
    }

    public static AgentBuilder Create(string name = "agent")
        => new AgentBuilder().WithName(name);

    public Task<AgentResponse> InvokeAsync(IContent input, CancellationToken ct = default)
        => InvokeAsyncInternal(input, null, ct);

    public async Task<T?> InvokeAsync<T>(IContent input, CancellationToken ct = default)
    {
        var response = await InvokeAsyncInternal(input, typeof(T), ct);
        var json = response.Text;
        
        if (string.IsNullOrWhiteSpace(json)) return default;
        return System.Text.Json.JsonSerializer.Deserialize<T>(json);
    }

    private async Task<AgentResponse> InvokeAsyncInternal(
        IContent input, 
        Type? outputType, 
        CancellationToken ct)
    {
        var turnMessages = new List<Message>();

        int inTokens = 0;
        int outTokens = 0;
        int reasoningTokens = 0;

        await foreach (var evt in CoreStreamAsync(input, outputType, turnMessages, ct))
        {
            if (evt is AgentErrorEvent error)
            {
                throw error.Error;
            }

            if (evt is LLMMetaEvent meta)
            {
                inTokens += meta.Usage.InputTokens;
                outTokens += meta.Usage.OutputTokens;
                reasoningTokens += meta.Usage.ReasoningTokens;
            }
        }

        var lastAssistantMsg = turnMessages
            .Where(m => m.Role == Role.Assistant)
            .LastOrDefault();

        IContent content = lastAssistantMsg?.Contents.OfType<Text>().LastOrDefault()
            ?? (lastAssistantMsg?.Contents.LastOrDefault() ?? new Text(""));

        var response = new AgentResponse(
            content,
            new TokenUsage(inTokens, outTokens, reasoningTokens));

        return response;
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
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["Agent"] = _config.Name
        }))
        {
            var userMessage = new Message(Role.User, input);

            // Recall relevant contextual messages (which may include history, summaries, facts, etc.)
            IReadOnlyList<Message> recalled = [];
            try
            {
                recalled = await _memory.RecallAsync(
                    userMessage, 
                    new TokenBudget(_baseOptions.ContextWindow ?? 0), 
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Memory recall failed — continuing with empty context.");
            }

            _logger.LogInformation("Agent invoked: InputLength={Len} MemoryType={MemType} ContextLimit={CtxLimit}",
                input.ForLlm().Length, _memory.GetType().Name, _baseOptions.ContextWindow ?? 0);

            // Current turn working context: we start with the user message
            var workingChat = new List<Message> { userMessage };

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
                                pendingToolCalls[tc.Call.Id] = tc.Call;
                                var argsJson = tc.Call.Arguments?.ToString() ?? "{}";
                                _logger.LogInformation("Tool called: {ToolName} Args={Args}", tc.Call.Name, argsJson.Length > 200 ? argsJson[..200] + "..." : argsJson);
                                
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
                    yield return new AgentErrorEvent(capturedEx);
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
                        await _memory.RememberAsync(workingChat, ct).ConfigureAwait(false);
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

                foreach (var result in results)
                {
                    var toolName = pendingToolCalls.TryGetValue(result.CallId, out var tc) ? tc.Name : "unknown";
                    var resultLength = result.ForLlm()?.Length ?? 0;
                    
                    _logger.LogDebug("Tool result: {ToolName} Duration={Ms}ms ResultLength={Len}", toolName, toolDuration, resultLength);
                    
                    workingChat.Add(new Message(Role.Tool, result));
                    yield return new AgentToolResultEvent(result);
                }

                pendingToolCalls.Clear();
            }
        }
    }
}
