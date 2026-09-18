using System.Reflection;
using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tool;
using AgentCore.Tool.Tools;

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

public sealed class Agent : IAgent
{
    private static readonly IReadOnlyList<IContent> DefaultInstructions = [new Text("You are a helpful AI assistant.")];
    private readonly IAgentEngine _engine;

    public IContext Context { get; }
    public ILLM LLM { get; }
    public ITooling Tooling { get; }
    public IReadOnlyList<ITool> Tools { get; }
    public IReadOnlyList<IContent> Instructions { get; }
    public int MaxIterations { get; }
    public IReadOnlyList<ToolDefinition> ToolDefinitions => Tools.Select(t => t.Info).ToArray();

    public Agent(
        ILLM llm,
        IReadOnlyList<ITool>? tools = null,
        IContext? context = null,
        ITooling? tooling = null,
        IReadOnlyList<IContent>? instructions = null,
        int maxIterations = 20,
        IAgentEngine? engine = null)
    {
        LLM = llm ?? throw new ArgumentNullException(nameof(llm));
        Tools = tools ?? [];
        Context = context ?? new ChatContext();
        Tooling = tooling ?? new Tooling();
        Instructions = instructions ?? DefaultInstructions;
        MaxIterations = maxIterations;
        _engine = engine ?? AgentEngine.Instance;
    }

    public Agent WithLLM(ILLM newLlm)
        => new(newLlm, Tools, Context, Tooling, Instructions, MaxIterations, _engine);

    public Agent WithTools(params ITool[] newTools)
        => new(LLM, newTools ?? throw new ArgumentNullException(nameof(newTools)), Context, Tooling, Instructions, MaxIterations, _engine);

    public Agent WithTools(IEnumerable<ITool> newTools)
        => new(LLM, (newTools ?? throw new ArgumentNullException(nameof(newTools))).ToArray(), Context, Tooling, Instructions, MaxIterations, _engine);

    public Agent WithTools<T>() => WithTools(typeof(T));

    public Agent WithTools(object instance, params IMetadata[] metadata)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var type = instance as Type ?? instance.GetType();
        var target = instance is Type ? null : instance;
        var flags = BindingFlags.Public | BindingFlags.Static | (target != null ? BindingFlags.Instance : 0);

        var extracted = type.GetMethods(flags)
            .Where(m => m.GetCustomAttribute<ToolAttribute>() != null)
            .Select(m => (ITool)new MethodTool(m, m.IsStatic ? null : target, extraMetadata: metadata));

        return WithTools([.. Tools, .. extracted]);
    }

    public Agent WithContext(IContext newContext)
        => new(LLM, Tools, newContext ?? throw new ArgumentNullException(nameof(newContext)), Tooling, Instructions, MaxIterations, _engine);

    public Agent WithTooling(ITooling newTooling)
        => new(LLM, Tools, Context, newTooling ?? throw new ArgumentNullException(nameof(newTooling)), Instructions, MaxIterations, _engine);

    public Agent WithInstructions(IEnumerable<IContent> newInstructions)
        => new(LLM, Tools, Context, Tooling, (newInstructions ?? throw new ArgumentNullException(nameof(newInstructions))).ToArray(), MaxIterations, _engine);

    public Agent WithEngine(IAgentEngine newEngine)
        => new(LLM, Tools, Context, Tooling, Instructions, MaxIterations, newEngine ?? throw new ArgumentNullException(nameof(newEngine)));

    public IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        IReadOnlyList<IContent> input,
        CancellationToken ct = default)
        => _engine.RunAsync(Context, LLM, Tooling, Tools, Instructions, input, MaxIterations, ct);
}
