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
    int maxIterations = 20,
    IAgentEngine? engine = null) : IAgent
{
    public ILLM LLM { get; } = llm ?? throw new ArgumentNullException(nameof(llm));
    public IReadOnlyList<ITool> Tools { get; } = tools ?? [];
    public IContext Context { get; } = context ?? new ChatContext();
    public ITooling Tooling { get; } = tooling ?? new Tooling();
    public IReadOnlyList<IContent> Instructions { get; } = instructions ?? [new Text("You are a helpful AI assistant.")];
    public int MaxIterations { get; } = maxIterations;
    public IReadOnlyList<ToolDefinition> ToolDefinitions => Tools.Select(t => t.Info).ToArray();

    private Agent Clone(ILLM? l = null, IReadOnlyList<ITool>? t = null, IContext? c = null, ITooling? tl = null, IReadOnlyList<IContent>? i = null, IAgentEngine? e = null)
        => new(l ?? LLM, t ?? Tools, c ?? Context, tl ?? Tooling, i ?? Instructions, MaxIterations, e ?? engine);

    public Agent UseLLM(ILLM newLlm) => Clone(l: newLlm ?? throw new ArgumentNullException(nameof(newLlm)));
    public Agent UseContext(IContext newCtx) => Clone(c: newCtx ?? throw new ArgumentNullException(nameof(newCtx)));
    public Agent UseTooling(ITooling newTooling) => Clone(tl: newTooling ?? throw new ArgumentNullException(nameof(newTooling)));
    public Agent UseInstructions(IReadOnlyList<IContent> inst) => Clone(i: inst ?? throw new ArgumentNullException(nameof(inst)));
    public Agent UseEngine(IAgentEngine newEngine) => Clone(e: newEngine ?? throw new ArgumentNullException(nameof(newEngine)));

    public Agent AddTools(params ITool[] newTools) => Clone(t: [.. Tools, .. newTools ?? throw new ArgumentNullException(nameof(newTools))]);
    public Agent AddTools(IEnumerable<ITool> newTools) => Clone(t: [.. Tools, .. newTools ?? throw new ArgumentNullException(nameof(newTools))]);
    public Agent RemoveTool(string name) => Clone(t: Tools.Where(t => !string.Equals(t.Info.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray());
    public Agent RemoveTools(Func<ITool, bool> predicate) => Clone(t: Tools.Where(t => !(predicate ?? throw new ArgumentNullException(nameof(predicate)))(t)).ToArray());

    public Agent AddLayer(ContextLayer layer) { ArgumentNullException.ThrowIfNull(layer); layer.Attach(Context); return UseContext(layer); }
    public Agent AddLayer(LLMLayer layer) { ArgumentNullException.ThrowIfNull(layer); layer.Attach(LLM); return UseLLM(layer); }
    public Agent AddLayer(ToolingLayer layer) { ArgumentNullException.ThrowIfNull(layer); layer.Attach(Tooling); return UseTooling(layer); }

    public IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(IReadOnlyList<IContent> input, CancellationToken ct = default)
        => (engine ?? AgentEngine.Instance).RunAsync(Context, LLM, Tooling, Tools, Instructions, input, MaxIterations, ct);
}
