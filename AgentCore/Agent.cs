using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tool;
using System.Runtime.CompilerServices;

namespace AgentCore;

public interface IAgent
{
    IReadOnlyList<IContent> Instructions { get; }
    IReadOnlyList<ToolDefinition> ToolDefinitions { get; }
    IContext Context { get; }

    IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        IReadOnlyList<IContent> input,
        CancellationToken ct = default);
}

public sealed class Agent(
    IContext context,
    ILLM llm,
    ITooling tooling,
    IReadOnlyList<ITool> tools,
    IReadOnlyList<IContent> instructions,
    int maxIterations) : IAgent
{
    public IContext Context => context;
    public ILLM LLM => llm;
    public ITooling Tooling => tooling;
    public IReadOnlyList<ITool> Tools => tools;
    public IReadOnlyList<IContent> Instructions => instructions;
    public int MaxIterations => maxIterations;

    public IReadOnlyList<ToolDefinition> ToolDefinitions => tools.Select(t => t.Info).ToArray();

    public static AgentBuilder Create() => new();

    public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        IReadOnlyList<IContent> input,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await Context.WriteAsync(new Message(Role.User, input), ct).ConfigureAwait(false);
        var messages = await Context.ReadAsync(ct).ConfigureAwait(false);

        int iterations = 0;
        List<ToolCall>? toolCalls;
        do
        {
            ct.ThrowIfCancellationRequested();
            if (++iterations > maxIterations)
                throw new InvalidOperationException($"Execution exceeded maximum limit of {maxIterations} iterations.");

            List<Message> prompt = [new Message(Role.System, Instructions), .. messages];

            toolCalls = null;
            await foreach (var evt in Context.WriteAsync(llm.GenerateAsync(prompt, ToolDefinitions, ct: ct), ct))
            {
                if (evt is ToolCall tc) (toolCalls ??= []).Add(tc);
                yield return evt;
            }

            if (toolCalls is not null)
            {
                await foreach (var evt in Context.WriteAsync(tooling.ExecuteAsync(toolCalls, tools, ct), ct))
                {
                    yield return evt;
                }

                messages = await Context.ReadAsync(ct).ConfigureAwait(false);
            }
        } while (toolCalls is not null);
    }
}
