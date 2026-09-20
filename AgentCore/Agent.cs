using System.Runtime.CompilerServices;
using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tool;

namespace AgentCore;

public interface IAgent
{
    IReadOnlyList<IContent> Instructions { get; }
    IContext Context { get; }
    ILLM LLM { get; }
    IToolbox Toolbox { get; }

    IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        IReadOnlyList<IContent> input,
        CancellationToken ct = default);
}

public sealed class Agent(
    ILLM llm,
    IToolbox? toolbox = null,
    IContext? context = null,
    IReadOnlyList<IContent>? instructions = null,
    int maxIterations = 20) : IAgent
{
    public ILLM LLM { get; } = llm?.GetType() == typeof(LLMLayer) ? (LLMLayer)llm : new LLMLayer(llm ?? throw new ArgumentNullException(nameof(llm)));
    public IToolbox Toolbox { get; } = toolbox?.GetType() == typeof(ToolboxLayer) ? (ToolboxLayer)toolbox : new ToolboxLayer(toolbox ?? new Toolbox());
    public IContext Context { get; } = context?.GetType() == typeof(ContextLayer) ? (ContextLayer)context : new ContextLayer(context ?? new ChatContext());
    public IReadOnlyList<IContent> Instructions { get; } = instructions ?? [new Text("You are a helpful AI assistant.")];
    public int MaxIterations { get; } = maxIterations;

    public Agent(
        ILLM llm,
        Func<IToolbox, IToolbox>? toolbox = null,
        Func<IContext, IContext>? context = null,
        IReadOnlyList<IContent>? instructions = null,
        int maxIterations = 20)
        : this(
            llm,
            toolbox != null ? toolbox(new Toolbox()) : null,
            context != null ? context(new ChatContext()) : null,
            instructions,
            maxIterations)
    {
    }

    public Agent With(
        ILLM? llm = null,
        IToolbox? toolbox = null,
        IContext? context = null,
        IReadOnlyList<IContent>? instructions = null,
        int? maxIterations = null)
        => new(
            llm ?? LLM,
            toolbox ?? Toolbox,
            context ?? Context,
            instructions ?? Instructions,
            maxIterations ?? MaxIterations);

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
            if (++iterations > MaxIterations)
                throw new InvalidOperationException($"Execution exceeded maximum limit of {MaxIterations} iterations.");

            var toolDefs = await Toolbox.GetDefinitionsAsync(ct).ConfigureAwait(false);
            List<Message> prompt = [new Message(Role.System, Instructions), .. messages];

            toolCalls = null;
            await foreach (var evt in Context.WriteAsync(LLM.GenerateAsync(prompt, toolDefs, ct: ct), ct))
            {
                if (evt is ToolCall tc) (toolCalls ??= []).Add(tc);
                yield return evt;
            }

            if (toolCalls is not null)
            {
                await foreach (var evt in Context.WriteAsync(Toolbox.ExecuteAsync(toolCalls, ct), ct))
                    yield return evt;

                messages = await Context.ReadAsync(ct).ConfigureAwait(false);
            }
        } while (toolCalls is not null);
    }
}
