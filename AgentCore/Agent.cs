using System.Runtime.CompilerServices;
using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tool;

namespace AgentCore;

public interface IAgent
{
    IReadOnlyList<IContent> Instructions { get; }
    IReadOnlyList<ToolDefinition> ToolDefinitions { get; }
    IContext Context { get; }
    ILLM LLM { get; }
    ITooling Tooling { get; }
    IReadOnlyList<ITool> Tools { get; }

    IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        IReadOnlyList<IContent> input,
        CancellationToken ct = default);
}

public sealed class Agent(
    ILLM llm,
    IReadOnlyList<ITool>? tools = null,
    IContext? context = null,
    ITooling? tooling = null,
    IReadOnlyList<IContent>? instructions = null,
    int maxIterations = 20) : IAgent
{
    public ILLM LLM { get; } = llm ?? throw new ArgumentNullException(nameof(llm));
    public IReadOnlyList<ITool> Tools { get; } = tools ?? [];
    public IContext Context { get; } = context ?? new ChatContext();
    public ITooling Tooling { get; } = tooling ?? new Tooling();
    public IReadOnlyList<IContent> Instructions { get; } = instructions ?? [new Text("You are a helpful AI assistant.")];
    public int MaxIterations { get; } = maxIterations;
    public IReadOnlyList<ToolDefinition> ToolDefinitions => Tools.Select(t => t.Info).ToArray();

    public Agent With(
        ILLM? llm = null,
        IReadOnlyList<ITool>? tools = null,
        IContext? context = null,
        ITooling? tooling = null,
        IReadOnlyList<IContent>? instructions = null,
        int? maxIterations = null)
        => new(
            llm ?? LLM,
            tools ?? Tools,
            context ?? Context,
            tooling ?? Tooling,
            instructions ?? Instructions,
            maxIterations ?? MaxIterations);

    public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        IReadOnlyList<IContent> input,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var toolDefs = ToolDefinitions;
        await Context.WriteAsync(new Message(Role.User, input), ct).ConfigureAwait(false);
        var messages = await Context.ReadAsync(ct).ConfigureAwait(false);

        int iterations = 0;
        List<ToolCall>? toolCalls;
        do
        {
            ct.ThrowIfCancellationRequested();
            if (++iterations > MaxIterations)
                throw new InvalidOperationException($"Execution exceeded maximum limit of {MaxIterations} iterations.");

            List<Message> prompt = [new Message(Role.System, Instructions), .. messages];

            toolCalls = null;
            await foreach (var evt in Context.WriteAsync(LLM.GenerateAsync(prompt, toolDefs, ct: ct), ct))
            {
                if (evt is ToolCall tc) (toolCalls ??= []).Add(tc);
                yield return evt;
            }

            if (toolCalls is not null)
            {
                await foreach (var evt in Context.WriteAsync(Tooling.ExecuteAsync(toolCalls, Tools, ct), ct))
                    yield return evt;

                messages = await Context.ReadAsync(ct).ConfigureAwait(false);
            }
        } while (toolCalls is not null);
    }
}
