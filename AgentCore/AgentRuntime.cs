using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Context;
using AgentCore.Memory;
using AgentCore.Tooling;
using AgentCore.Tokens;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore;

/// <summary>
/// The core runtime orchestrator that implements the message-oriented tool-using run loop.
/// </summary>
public sealed class AgentRuntime : IAgentRuntime
{
    private readonly IContextManager _contextManager;
    private readonly IAgentMemory? _memory;
    private readonly LLMOptions _baseOptions;
    private readonly ITokenCounter _tokenCounter;
    private readonly AgentConfig _config;
    private readonly ILogger<AgentRuntime> _logger;

    public AgentRuntime(
        IContextManager contextManager,
        IAgentMemory? memory,
        LLMOptions baseOptions,
        ITokenCounter tokenCounter,
        AgentConfig config,
        ILogger<AgentRuntime> logger)
    {
        _contextManager = contextManager ?? throw new ArgumentNullException(nameof(contextManager));
        _memory = memory;
        _baseOptions = baseOptions ?? throw new ArgumentNullException(nameof(baseOptions));
        _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async IAsyncEnumerable<AgentEvent> RunAsync(
        List<Message> state,
        ILLMExecutor llm,
        IToolExecutor tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        int consecutiveToolSteps = 0;
        int totalToolCalls = 0;
        int lastLlmTokens = 0;

        _logger.LogInformation("Orchestration loop started: Agent={AgentName} InitialStateMessagesCount={Count}", _config.Name, state.Count);

        // 1. Recall memory before first LLM step
        List<Message> memoryMessages = [];
        if (_memory != null)
        {
            try
            {
                var recalled = await _memory.RecallAsync(state, ct);
                if (recalled.Count > 0)
                {
                    memoryMessages.AddRange(recalled);
                    var recalledText = string.Join("\n\n", recalled.Select(m => string.Join("", m.Contents.Select(c => c.ForLlm()))));
                    _logger.LogDebug("Memory recall result: ContentCount={Count} ContentPreview={Preview}",
                        recalled.Count, recalledText.Length > 200 ? recalledText[..200] + "..." : recalledText);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Memory recall failed — continuing without memory context.");
            }
        }

        var workingChat = new List<Message>(state);
        var pendingToolCalls = new Dictionary<string, ToolCall>();
        var textBuffer = new StringBuilder();
        var reasoningBuffer = new StringBuilder();
        var toolCallsBuffer = new List<ToolCall>();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (consecutiveToolSteps >= (_config.MaxToolCalls ?? 10))
            {
                _logger.LogWarning("Execution halted: Exceeded consecutive tool call limit of {Limit}", _config.MaxToolCalls ?? 10);
                yield return new TextEvent("You have exceeded the maximum allowed consecutive tool calls. Stop calling tools and respond to the user immediately.");
                break;
            }

            var runningTools = new List<Task<ToolResult>>();

            // 2. Assemble and fit context using IContextManager
            var combinedMessages = new List<Message>();
            if (memoryMessages.Count > 0)
            {
                combinedMessages.AddRange(memoryMessages);
            }
            combinedMessages.AddRange(workingChat);

            int tokenBudget = _baseOptions.ContextLength ?? 8000;
            var managedMessages = _contextManager.Manage(combinedMessages, tokenBudget);

            _logger.LogInformation("LLM orchestration step {Step}: MessagesCount={MsgCount} Tokens≈{Approx} Budget={Budget}",
                consecutiveToolSteps + 1, managedMessages.Count, lastLlmTokens, tokenBudget);

            var enumerator = llm.StreamAsync(managedMessages, null!, null, ct).GetAsyncEnumerator(ct);
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
                            _logger.LogInformation("Tool call scheduled by LLM: {ToolName} CallId={CallId} Args={Args}", 
                                tc.Call.Name, tc.Call.Id, argsJson.Length > 200 ? argsJson[..200] + "..." : argsJson);

                            runningTools.Add(tools.HandleToolCallAsync(tc.Call, ct));
                            break;

                        case LLMMetaEvent meta:
                            if (meta.Usage.IsEmpty)
                            {
                                lastLlmTokens = await _tokenCounter.CountAsync(workingChat, ct).ConfigureAwait(false) + meta.ToolSchemaTokens;
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

            // 3. Handle context limits overflow recovery
            if (limitsExceeded)
            {
                int currentTokensCount = await _tokenCounter.CountAsync(combinedMessages, ct);
                int budget = _baseOptions.ContextLength.HasValue
                    ? _baseOptions.ContextLength.Value - 1024  // Reserve space for response
                    : (int)(currentTokensCount * 0.7);

                _logger.LogWarning("Context limit exceeded: CurrentTokens={Current} Budget={Budget}. Triggering ContextManager reduction.", currentTokensCount, budget);

                var reduced = _contextManager.Manage(workingChat, budget);
                workingChat = [.. reduced];

                state.Clear();
                state.AddRange(workingChat);

                textBuffer.Clear();
                reasoningBuffer.Clear();
                toolCallsBuffer.Clear();
                pendingToolCalls.Clear();
                runningTools.Clear();

                continue;
            }

            // 4. Evolve state: Append Assistant content
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

                var assistantMessage = new Message(Role.Assistant, contents);
                workingChat.Add(assistantMessage);
                state.Add(assistantMessage);
            }

            // 5. If no tools are executed, we are done
            if (runningTools.Count == 0)
            {
                _logger.LogInformation("Orchestration loop completed: TotalConsecutiveSteps={Steps} TotalToolCalls={Count}", consecutiveToolSteps, totalToolCalls);

                // Semantic memory retain
                if (_memory != null)
                {
                    var finalAssistantMsg = state.LastOrDefault(m => m.Role == Role.Assistant);
                    var userMsg = state.FirstOrDefault(m => m.Role == Role.User);
                    var turnSnapshot = new List<Message>();
                    if (userMsg != null) turnSnapshot.Add(userMsg);
                    if (finalAssistantMsg != null) turnSnapshot.Add(finalAssistantMsg);

                    if (turnSnapshot.Count > 0)
                    {
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
                }
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
                var tc = pendingToolCalls.TryGetValue(result.CallId, out var tcCall) ? tcCall : null;
                var toolName = tc?.Name ?? "unknown";
                var resultLength = result.ForLlm()?.Length ?? 0;

                if (result.Result is ToolExecutionException tex && tex.Message.Contains("requires approval"))
                {
                    pendingApprovals.Add(result.CallId);
                }

                _logger.LogInformation("Tool result received: {ToolName} CallId={CallId} Duration={Ms}ms ResultLength={Len}", 
                    toolName, result.CallId, toolDuration, resultLength);

                var toolMessage = new Message(Role.Tool, result);
                workingChat.Add(toolMessage);
                state.Add(toolMessage);
                yield return new AgentToolResultEvent(result);
            }

            // 6. Pause execution for pending approvals
            if (pendingApprovals.Count > 0)
            {
                _logger.LogWarning("Orchestration paused: {Count} tool execution(s) require manual user approval. Awaiting ResumeAsync.", pendingApprovals.Count);
                break;
            }

            pendingToolCalls.Clear();
        }
    }
}
