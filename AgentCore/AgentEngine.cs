using System.Runtime.CompilerServices;
using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tool;

namespace AgentCore;

public interface IAgentEngine
{
    IAsyncEnumerable<IContentEvent> RunAsync(
        IContext context,
        ILLM llm,
        ITooling tooling,
        IReadOnlyList<ITool> tools,
        IReadOnlyList<IContent> instructions,
        IReadOnlyList<IContent> input,
        int maxIterations = 20,
        CancellationToken ct = default);
}

public sealed class AgentEngine : IAgentEngine
{
    public static readonly AgentEngine Instance = new();

    public async IAsyncEnumerable<IContentEvent> RunAsync(
        IContext context,
        ILLM llm,
        ITooling tooling,
        IReadOnlyList<ITool> tools,
        IReadOnlyList<IContent> instructions,
        IReadOnlyList<IContent> input,
        int maxIterations = 20,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(tooling);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(input);

        var toolDefinitions = tools.Select(t => t.Info).ToArray();

        await context.WriteAsync(new Message(Role.User, input), ct).ConfigureAwait(false);
        var messages = await context.ReadAsync(ct).ConfigureAwait(false);

        int iterations = 0;
        List<ToolCall>? toolCalls;
        do
        {
            ct.ThrowIfCancellationRequested();
            if (++iterations > maxIterations)
                throw new InvalidOperationException($"Execution exceeded maximum limit of {maxIterations} iterations.");

            List<Message> prompt = [new Message(Role.System, instructions), .. messages];

            toolCalls = null;
            await foreach (var evt in context.WriteAsync(llm.GenerateAsync(prompt, toolDefinitions, ct: ct), ct))
            {
                if (evt is ToolCall tc) (toolCalls ??= []).Add(tc);
                yield return evt;
            }

            if (toolCalls is not null)
            {
                await foreach (var evt in context.WriteAsync(tooling.ExecuteAsync(toolCalls, tools, ct), ct))
                {
                    yield return evt;
                }

                messages = await context.ReadAsync(ct).ConfigureAwait(false);
            }
        } while (toolCalls is not null);
    }
}
