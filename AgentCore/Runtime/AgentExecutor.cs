using AgentCore.Chat;
using AgentCore.LLM.Execution;
using AgentCore.LLM.Protocol;
using AgentCore.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace AgentCore.Runtime;

public interface IAgentExecutor
{
    IAsyncEnumerable<string> ExecuteStreamingAsync(IAgentContext ctx, CancellationToken ct = default);
}

public sealed class ToolCallingLoop(ILogger<ToolCallingLoop> _logger) : IAgentExecutor
{
    public async IAsyncEnumerable<string> ExecuteStreamingAsync(
        IAgentContext ctx,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ctx.ScratchPad.AddUser(ctx.UserInput ?? "No User input.");

        var llm = ctx.Services.GetRequiredService<ILLMExecutor>();
        var tools = ctx.Services.GetRequiredService<IToolCatalog>();
        var runtime = ctx.Services.GetRequiredService<IToolExecutor>();

        for (int i = 0; i < ctx.Config.MaxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();

            var request = new LLMRequest(ctx.ScratchPad, ToolCallMode.Auto)
            {
                AvailableTools = tools.RegisteredTools
            };

            await foreach (var delta in llm.StreamAsync(request, ct))
            {
                if (delta is TextDelta textDelta)
                    yield return textDelta.Value;
            }

            var response = llm.Response;
            if (!response.HasToolCall) yield break;

            _logger.LogInformation("Tool called: {ToolName}", response.ToolCall!.Name);
            var toolResult = await runtime.HandleToolCallAsync(response.ToolCall, ct);
            ctx.ScratchPad.AppendToolResult(toolResult);
        }

        _logger.LogWarning("Agent loop stopped: max iterations reached ({MaxIterations})", ctx.Config.MaxIterations);
    }
}
