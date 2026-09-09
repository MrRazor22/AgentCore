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

        for (int i = 0; i < maxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();
            var messages = await context.GetMessagesAsync(ct);
            var assembler = new BlockAssembler();

            await foreach (var evt in llm.StreamAsync(messages, responseSchema, tooling.GetDefinitions(), ct))
            {
                assembler.Push(evt);
                yield return evt;
            }

            var assistantMsg = assembler.ToMessage();
            await context.AddAsync([assistantMsg], ct);

            var toolCalls = assistantMsg.Contents.OfType<ToolCall>().ToList();
            if (toolCalls.Count == 0) yield break;

            foreach (var call in toolCalls)
            {
                yield return call;
            }

            var results = await Task.WhenAll(toolCalls.Select(tc => tooling.ExecuteAsync(tc, ct)));
            await context.AddAsync([new Message(Role.Tool, [.. results])], ct);

            foreach (var result in results)
            {
                yield return result;
            }
        }

        throw new InvalidOperationException($"Execution exceeded maximum limit of {maxIterations} iterations.");
    }
}
