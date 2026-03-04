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

public sealed class ToolCallingLoop(IAgentMemory _memory, ILogger<ToolCallingLoop> _logger) : IAgentExecutor
{
    public async IAsyncEnumerable<string> ExecuteStreamingAsync(
        IAgentContext ctx,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ctx.Messages.AddUser(ctx.UserInput ?? "No User input.");

        var llm = ctx.Services.GetRequiredService<ILLMExecutor>();
        var runtime = ctx.Services.GetRequiredService<IToolExecutor>();

        var options = new LLMOptions
        {
            ToolCallMode = ToolCallMode.Auto,
            ResponseSchema = ctx.Config.OutputType?.GetSchemaForType()
        };

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var textBuffer = new StringBuilder();
            var runningTools = new List<Task<ToolResult>>();

            await foreach (var evt in llm.StreamAsync([.. ctx.Messages], options, ct))
            {
                switch (evt)
                {
                    case TextEvent t:
                        textBuffer.Append(t.Delta);
                        yield return t.Delta;
                        break;

                    case ToolCallEvent tc:
                        ctx.Messages.AddAssistantToolCall(tc.Call);
                        _logger.LogInformation("Tool called: {ToolName}", tc.Call.Name);
                        runningTools.Add(runtime.HandleToolCallAsync(tc.Call, ct));
                        break;
                }
            }

            if (runningTools.Count == 0)
            {
                ctx.Messages.AddAssistant(textBuffer.ToString().Trim());
                await _memory.UpdateAsync(ctx.SessionId, ctx.Messages);
                break;
            }

            var results = await Task.WhenAll(runningTools);

            foreach (var result in results)
                ctx.Messages.AddToolResult(result);

            // Checkpoint after the turn completes
            await _memory.UpdateAsync(ctx.SessionId, ctx.Messages);
        }
    }
}
