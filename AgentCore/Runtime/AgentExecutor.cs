using AgentCore.Chat;
using AgentCore.Diagnostics;
using AgentCore.Execution;
using AgentCore.Json;
using AgentCore.LLM;
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
    private readonly ILogger<ToolCallingLoop> _logger;
    private readonly PipelineHandler<IAgentContext, IAsyncEnumerable<string>> _pipeline;
    private readonly int _maxToolSteps;

    public ToolCallingLoop(
        IAgentMemory memory,
        ILLMExecutor llm,
        IToolExecutor runtime,
        ILogger<ToolCallingLoop> logger,
        IEnumerable<PipelineMiddleware<IAgentContext, IAsyncEnumerable<string>>>? middlewares = null,
        int maxToolSteps = 15)
    {
        _memory = memory;
        _llm = llm;
        _runtime = runtime;
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
        ctx.Messages.AddUser(ctx.UserInput ?? "No User input.");

        var options = new LLMOptions
        {
            ToolCallMode = ToolCallMode.Auto,
            ResponseSchema = ctx.OutputType?.GetSchemaForType()
        };

        int consecutiveToolSteps = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (consecutiveToolSteps >= _maxToolSteps)
            {
                _logger.LogWarning("Execution breached max tool steps ({MaxSteps})", _maxToolSteps);
                ctx.Messages.AddSystem("You have exceeded the maximum allowed consecutive tool calls. Stop calling tools and respond to the user immediately.");
            }

            if (consecutiveToolSteps > _maxToolSteps)
            {
                 throw new InvalidOperationException($"Agent Execution exceeded max tool steps of {_maxToolSteps}");
            }

            var textBuffer = new StringBuilder();
            var runningTools = new List<Task<ToolResult>>();

            await foreach (var evt in _llm.StreamAsync([.. ctx.Messages], options, ct))
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
                            ctx.Messages.AddAssistant(textBuffer.ToString());
                            textBuffer.Clear();
                        }

                        ctx.Messages.AddAssistantToolCall(tc.Call);
                        _logger.LogInformation("Tool called: {ToolName}", tc.Call.Name);
                        runningTools.Add(_runtime.HandleToolCallAsync(tc.Call, ct));
                        break;
                }
            }

            if (runningTools.Count == 0)
            {
                if (textBuffer.Length > 0)
                {
                    ctx.Messages.AddAssistant(textBuffer.ToString().Trim());
                }
                await _memory.UpdateAsync(ctx.SessionId, ctx.Messages);
                break;
            }

            consecutiveToolSteps++;

            var results = await Task.WhenAll(runningTools);

            foreach (var result in results)
                ctx.Messages.AddToolResult(result);

            // Checkpoint after the turn completes
            await _memory.UpdateAsync(ctx.SessionId, ctx.Messages);
        }
    }
}
