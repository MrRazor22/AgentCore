using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM;
using AgentCore.Tooling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

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

        for (int i = 0; i < ctx.Config.MaxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();

            var content = llm.StreamAsync([.. ctx.ScratchPad], options with { OutputType = ctx.Config.OutputType }, ct);

            var textBuffer = "";
            var toolId = "";
            var toolName = "";
            var toolArgs = "";

            await foreach (var delta in content)
            {
                if (delta is TextDelta t)
                {
                    textBuffer += t.Value;
                    yield return t.Value;
                }
                if (delta is ToolCallDelta tc)
                {
                    if (!string.IsNullOrEmpty(tc.Id)) toolId = tc.Id;
                    if (!string.IsNullOrEmpty(tc.Name)) toolName = tc.Name;
                    if (!string.IsNullOrEmpty(tc.ArgumentsDelta)) toolArgs += tc.ArgumentsDelta;
                }
            }

            IContent assistantContent;
            if (!string.IsNullOrEmpty(toolName))
            {
                JsonObject? args;
                if (!string.IsNullOrEmpty(toolArgs) && toolArgs.TryParseCompleteJson(out var parsed))
                    args = parsed ?? new JsonObject();
                else
                    args = new JsonObject();
                assistantContent = new ToolCall(toolId, toolName, args);
            }
            else
            {
                assistantContent = new Text(textBuffer.Trim());
            }

            var assistantMsg = new Message(Role.Assistant, assistantContent);
            ctx.ScratchPad.Add(assistantMsg);

            if (assistantContent is not ToolCall tc2) yield break;

            _logger.LogInformation("Tool called: {ToolName}", tc2.Name);
            var toolResult = await runtime.HandleToolCallAsync(tc2, ct);
            ctx.ScratchPad.AppendToolResult(toolResult);
        }

        _logger.LogWarning("Agent loop stopped: max iterations reached ({MaxIterations})", ctx.Config.MaxIterations);
    }
}
