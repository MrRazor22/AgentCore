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
            var assembler = new BlockAssembler();
            var assistant = new Message(Role.Assistant);
            await context.AddAsync([assistant], ct);

            var runningTools = new List<Task<ToolResult>>();

            await foreach (var evt in llm.StreamAsync(messages, responseSchema, tooling.GetDefinitions(), ct))
            {
                assembler.Push(evt);
                yield return evt;

                if (evt is IBlockEndEvent end && assembler.CompleteBlock(end.Index) is { } content)
                {
                    assistant.Append(content);
                    await context.UpdateAsync(assistant, ct);
                    yield return content;

                    if (content is ToolCall tc && executedToolCallIds.Add(tc.Id))
                    {
                        runningTools.Add(tooling.ExecuteAsync(tc, ct));
                    }
                }
            }

            assistant.Metadata = [assembler.Metadata];
            await context.UpdateAsync(assistant, ct);

            var toolCalls = assistant.Contents.OfType<ToolCall>().ToList();
            if (toolCalls.Count == 0) yield break;

            var results = await Task.WhenAll(runningTools);
            await context.AddAsync([new Message(Role.Tool, [.. results])], ct);

            foreach (var result in results)
            {
                yield return result;
            }
        }

        throw new InvalidOperationException($"Execution exceeded maximum limit of {maxIterations} iterations.");
    }
}
