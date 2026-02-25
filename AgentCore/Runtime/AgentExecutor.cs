using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM;
using AgentCore.Tooling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;

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

        var options = new LLMOptions(
            ToolCallMode: ToolCallMode.Auto,
            ResponseSchema: ctx.Config.OutputType?.GetSchemaForType()
        );

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var textBuffer = new StringBuilder();
            var toolCalls = new List<ToolCall>();

            await foreach (var evt in llm.StreamAsync([.. ctx.ScratchPad], options, ct))
            {
                switch (evt)
                {
                    case TextEvent t:
                        textBuffer.Append(t.Delta);
                        yield return t.Delta;
                        break;

                    case ToolCallEvent tc:
                        toolCalls.Add(tc.Call);
                        break;
                }
            }

            if (toolCalls.Count == 0)
            {
                ctx.ScratchPad.AddAssistant(textBuffer.ToString().Trim());
                break;
            }

            foreach (var tc in toolCalls)
                ctx.ScratchPad.AddAssistantToolCall(tc);

            var results = await Task.WhenAll(
                toolCalls.Select(tc =>
                {
                    _logger.LogInformation("Tool called: {ToolName}", tc.Name);
                    return runtime.HandleToolCallAsync(tc, ct);
                })
            );

            foreach (var result in results)
                ctx.ScratchPad.AppendToolResult(result);
        }
    }
}
