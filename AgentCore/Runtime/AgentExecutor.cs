using AgentCore.Conversation;
using AgentCore.Diagnostics;
using AgentCore.Execution;
using AgentCore.Json;
using AgentCore.LLM;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;

namespace AgentCore.Runtime;

public interface IAgentExecutor
{
    IAsyncEnumerable<string> ExecuteStreamingAsync(IAgentContext ctx, CancellationToken ct = default);
}

public sealed class ToolCallingLoop : IAgentExecutor
{
    private readonly IAgentMemory _memory;
    private readonly ILLMExecutor _llm;
    private readonly IToolExecutor _runtime;
    private readonly IContextManager _ctxManager;
    private readonly ITokenCounter _tokenCounter;
    private readonly LLMOptions _baseOptions;
    private readonly ILogger<ToolCallingLoop> _logger;
    private readonly PipelineHandler<IAgentContext, IAsyncEnumerable<string>> _pipeline;
    private readonly int _maxToolSteps;

    private int _lastTotalTokens;

    public ToolCallingLoop(
        IAgentMemory memory,
        ILLMExecutor llm,
        IToolExecutor runtime,
        IContextManager contextManager,
        ITokenCounter tokenCounter,
        LLMOptions baseOptions,
        ILogger<ToolCallingLoop> logger,
        IEnumerable<PipelineMiddleware<IAgentContext, IAsyncEnumerable<string>>>? middlewares = null,
        int maxToolSteps = 15)
    {
        _memory = memory;
        _llm = llm;
        _runtime = runtime;
        _ctxManager = contextManager;
        _tokenCounter = tokenCounter;
        _baseOptions = baseOptions;
        _logger = logger;
        _maxToolSteps = maxToolSteps;

        _pipeline = Pipeline<IAgentContext, IAsyncEnumerable<string>>.Build(
            middlewares ?? [],
            ExecuteInternalAsync);
    }

    public IAsyncEnumerable<string> ExecuteStreamingAsync(
        IAgentContext ctx,
        CancellationToken ct = default) => _pipeline(ctx, ct);

    private async IAsyncEnumerable<string> ExecuteInternalAsync(
        IAgentContext ctx,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var userMessage = new Message(Role.User, new Text(ctx.UserInput ?? "No User input."));
        var pendingCalls = new Dictionary<string, Message>();
        var toolSteps = new List<(Message Call, Message Result)>();
        var textBuffer = new StringBuilder();
        var intermediateAssistant = (Message?)null;

        var options = new LLMOptions
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
            ResponseSchema = ctx.OutputType?.GetSchemaForType()
        };

        int consecutiveToolSteps = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            _lastTotalTokens = 0;

            if (consecutiveToolSteps >= _maxToolSteps)
            {
                _logger.LogWarning("Execution breached max tool steps ({MaxSteps})", _maxToolSteps);
                yield return "You have exceeded the maximum allowed consecutive tool calls. Stop calling tools and respond to the user immediately.";
                break;
            }

            if (consecutiveToolSteps > _maxToolSteps)
            {
                throw new InvalidOperationException($"Agent Execution exceeded max tool steps of {_maxToolSteps}");
            }

            var runningTools = new List<Task<ToolResult>>();

            await foreach (var evt in _llm.StreamAsync([.. ctx.Chat.ToMessages()], options, ct))
            {
                switch (evt)
                {
                    case TextEvent t:
                        textBuffer.Append(t.Delta);
                        yield return t.Delta;
                        break;

                    case ToolCallEvent tc:
                        if (textBuffer.Length > 0)
                        {
                            intermediateAssistant = new Message(Role.Assistant, new Text(textBuffer.ToString()));
                            textBuffer.Clear();
                        }

                        pendingCalls[tc.Call.Id] = new Message(Role.Assistant, tc.Call);
                        _logger.LogInformation("Tool called: {ToolName}", tc.Call.Name);
                        runningTools.Add(_runtime.HandleToolCallAsync(tc.Call, ct));
                        break;

                    case TokenUsageEvent tu:
                        _lastTotalTokens = tu.Usage.InputTokens + tu.Usage.OutputTokens;
                        if (_tokenCounter is ApproximateTokenCounter approx)
                            approx.Calibrate(ctx.Chat.ToMessages(), tu.Usage.InputTokens);
                        break;
                }
            }

            Message? assistantReply = textBuffer.Length > 0 ? new Message(Role.Assistant, new Text(textBuffer.ToString().Trim())) : null;

            if (runningTools.Count == 0)
            {
                ctx.Chat.Add(new Turn(userMessage, toolSteps, assistantReply));
                await _memory.UpdateAsync(ctx.SessionId, ctx.Chat);
                break;
            }

            consecutiveToolSteps++;

            var results = await Task.WhenAll(runningTools);

            foreach (var result in results)
            {
                var callId = result.CallId;
                if (pendingCalls.TryGetValue(callId, out var callMsg))
                {
                    toolSteps.Add((callMsg, new Message(Role.Tool, result)));
                }
            }

            if (intermediateAssistant != null)
            {
                toolSteps.Insert(0, (new Message(Role.User, new Text("")), intermediateAssistant));
                intermediateAssistant = null;
            }

            ctx.Chat.Add(new Turn(userMessage, toolSteps, assistantReply));

            int ctxLen = options.ContextLength ?? 0;
            int totalForCompaction = _lastTotalTokens > 0
                ? _lastTotalTokens
                : (ctxLen > 0 ? await _tokenCounter.CountAsync(ctx.Chat.ToMessages(), ct).ConfigureAwait(false) : 0);
            if (ctxLen > 0 && totalForCompaction >= (int)(ctxLen * 0.30))
            {
                _logger.LogDebug("Reactive context compaction triggered: tokens={Tokens}, context={CtxLen}", totalForCompaction, ctxLen);
                var reduced = await _ctxManager.ReduceAsync(ctx.Chat, totalForCompaction, options, ct).ConfigureAwait(false);
                ctx.Chat = reduced;
            }

            toolSteps = [];
            pendingCalls.Clear();
            textBuffer.Clear();

            await _memory.UpdateAsync(ctx.SessionId, ctx.Chat);
        }
    }
}
