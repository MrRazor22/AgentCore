using AgentCore.Chat;
using AgentCore.LLM;
using AgentCore.Tooling;
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
        var runtime = ctx.Services.GetRequiredService<IToolExecutor>();

        var options = new LLMOptions(ToolCallMode: AgentCore.LLM.ToolCallMode.Auto);

    nextIteration:
        ct.ThrowIfCancellationRequested();

        var textBuffer = "";
        IContent? assistantContent = null;

        await foreach (var evt in llm.StreamAsync([.. ctx.ScratchPad], options with { OutputType = ctx.Config.OutputType }, ct))
        {
            switch (evt)
            {
                case TextEvent t:
                    textBuffer += t.Delta;
                    yield return t.Delta;
                    break;

                case ToolCallEvent tc:
                    assistantContent = tc.Call;
                    break;

                case CompletedEvent:
                    break;
            }
        }

        if (assistantContent == null)
        {
            assistantContent = new Text(textBuffer.Trim());
        }

        var assistantMsg = new Message(Role.Assistant, assistantContent);
        ctx.ScratchPad.Add(assistantMsg);

        if (assistantContent is ToolCall tc2)
        {
            _logger.LogInformation("Tool called: {ToolName}", tc2.Name);
            var toolResult = await runtime.HandleToolCallAsync(tc2, ct);
            ctx.ScratchPad.AppendToolResult(toolResult);

            goto nextIteration;
        }
    }
}
