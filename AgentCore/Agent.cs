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

    IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        IReadOnlyList<IContent> input,
        CancellationToken ct = default);
}

public sealed class Agent(
    IContext context,
    ILLM llm,
    ITooling toolbox,
    IReadOnlyList<ITool> tools,
    IReadOnlyList<IContent> instructions,
    int maxIterations = 20) : IAgent
{
    public IContext Context => context;
    public ILLM LLM => llm;
    public ITooling Toolbox => toolbox;
    public IReadOnlyList<ITool> Tools => tools;
    public IReadOnlyList<IContent> Instructions => instructions;
    public int MaxIterations => maxIterations;

    public IReadOnlyList<ToolDefinition> ToolDefinitions
    {
        get
        {
            var discovery = tools.OfType<AgentCore.Layers.Tools.ToolDiscoveryTool>().FirstOrDefault();
            if (discovery != null)
            {
                discovery.CatalogProvider ??= () => tools.Select(t => t.Info).ToList();
                return tools.Where(t => t.Info.Name == discovery.Info.Name || discovery.IsActive(t.Info)).Select(t => t.Info).ToList();
            }
            return tools.Select(t => t.Info).ToList();
        }
    }

    public static AgentBuilder Create() => new();

    public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        IReadOnlyList<IContent> input,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var existing = await context.PrepareAsync(ct: ct).ConfigureAwait(false);
        var staged = new List<Message>();

        if (existing.LastOrDefault()?.Contents.LastOrDefault() is ToolCall)
        {
            var completedIds = existing
                .Where(m => m.Role == Role.Tool)
                .Select(m => m.Get<ToolCallId>()?.Value ?? m.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet();

            foreach (var call in existing[^1].Contents.OfType<ToolCall>().Where(c => !completedIds.Contains(c.Id)))
            {
                var interrupted = new Interrupted();
                staged.Add(new Message(Role.Tool, [new Text(interrupted.Reason)],
                    id: call.Id, metadata: [new ToolCallId(call.Id), interrupted]));
            }
        }

        staged.Add(new Message(Role.User, input));
        var messages = await context.PrepareAsync(staged, ct).ConfigureAwait(false);

        int iterations = 0;
        List<ToolCall>? toolCalls;
        do
        {
            ct.ThrowIfCancellationRequested();
            if (++iterations > maxIterations)
                throw new InvalidOperationException($"Execution exceeded maximum limit of {maxIterations} iterations.");

            List<Message> prompt = [new Message(Role.System, Instructions), .. messages];

            toolCalls = null;
            await foreach (var evt in context.IngestAsync(
                llm.GenerateAsync(prompt, ToolDefinitions, ct: ct), ct))
            {
                if (evt is ToolCall tc) (toolCalls ??= []).Add(tc);
                yield return evt;
            }

            if (toolCalls is not null)
            {
                await foreach (var evt in context.IngestAsync(
                    toolbox.ExecuteAsync(toolCalls, tools, ct), ct))
                {
                    yield return evt;
                }

                messages = await context.PrepareAsync(ct: ct).ConfigureAwait(false);
            }
        } while (toolCalls is not null);
    }
}
