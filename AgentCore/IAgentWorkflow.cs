using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using System.Runtime.CompilerServices;

namespace AgentCore;

public interface IAgentWorkflow
{
    IAsyncEnumerable<IAgentEvent> ExecuteAsync(
        IContext context,
        IContent input,
        JsonSchema? responseSchema,
        CancellationToken ct = default);
}

public class ReActWorkflow(ILLM llm, ITooling tooling, int maxIterations = 20) : IAgentWorkflow
{
    public async IAsyncEnumerable<IAgentEvent> ExecuteAsync(
        IContext context,
        IContent input,
        JsonSchema? responseSchema,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await context.AddAsync([new Message(Role.User, [input])], ct);
        var existing = await context.GetMessagesAsync(ct);
        var executedToolCallIds = existing
            .Where(m => m.Role == Role.Tool)
            .SelectMany(m => m.Contents)
            .OfType<ToolResult>()
            .Select(r => r.CallId)
            .ToHashSet();

        for (int i = 0; i < maxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();
            var messages = await context.GetMessagesAsync(ct);
            var assistant = new StreamingMessage(Role.Assistant);

            await foreach (var evt in llm.StreamAsync(messages, responseSchema, tooling.GetDefinitions(), ct))
            {
                yield return evt;
                if (assistant.Push(evt) is { } content)
                {
                    yield return content;
                }
            }

            if (assistant.Contents.Count == 0) yield break;

            await context.AddAsync([assistant], ct);

            var toolCalls = assistant.Contents
                .OfType<ToolCall>()
                .Where(tc => executedToolCallIds.Add(tc.Id))
                .ToList();

            if (toolCalls.Count == 0) yield break;

            var toolTasks = toolCalls.Select(async tc =>
            {
                var result = await tooling.ExecuteAsync(tc, ct);
                await context.AddAsync([new Message(Role.Tool, [result])], CancellationToken.None);
                return result;
            }).ToList();

            while (toolTasks.Count > 0)
            {
                var completed = await Task.WhenAny(toolTasks);
                toolTasks.Remove(completed);
                yield return await completed;
            }
        }

        throw new InvalidOperationException($"Execution exceeded maximum limit of {maxIterations} iterations.");
    }
}
