using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCore.Layers.Tools;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class Discoverable(string? domain = null) : Attribute, IMetadata
{
    public string? Domain { get; } = domain;
}

public sealed class ToolDiscoveryTool : ITool
{
    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);

    public Func<IReadOnlyList<ToolDefinition>>? CatalogProvider { get; set; }

    public ToolDefinition Definition { get; } = new(
        "search_tools",
        "Searches available tools in catalog by keyword or domain to activate them into context.",
        new JsonSchemaBuilder()
            .Type<object>()
            .AddProperty("query", new JsonSchemaBuilder().Type<string>().Description("Search keyword or domain").Build(), required: true)
            .Build());

    public bool IsActive(ToolDefinition tool) =>
        tool.Metadata.Get<Discoverable>() == null || _active.Contains(tool.Name);

    public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        JsonObject arguments,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        var query = arguments["query"]?.ToString();
        if (string.IsNullOrWhiteSpace(query))
        {
            yield return new Text("Please provide a search query.");
            yield break;
        }

        var catalog = CatalogProvider?.Invoke() ?? [];
        var matches = catalog
            .Where(t => !IsActive(t) && (
                t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (t.Metadata.Get<Discoverable>()?.Domain is { Length: > 0 } d &&
                 d.Contains(query, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        if (matches.Count == 0)
        {
            yield return new Text($"No tools found matching '{query}'.");
            yield break;
        }

        foreach (var m in matches) _active.Add(m.Name);

        yield return new Text($"Activated {matches.Count} tool(s):\n" +
            string.Join("\n", matches.Select(m => $"- {m.Name}: {m.Description}")));
    }
}

public sealed class ToolDiscoveryLayer(ToolDiscoveryTool tool, IToolbox? inner = null) : ToolboxLayer(inner)
{
    public ToolDiscoveryTool Tool { get; } = tool ?? throw new ArgumentNullException(nameof(tool));

    public override async ValueTask<IReadOnlyList<ToolDefinition>> GetDefinitionsAsync(CancellationToken ct = default)
    {
        var allDefs = Inner != null ? await Inner.GetDefinitionsAsync(ct).ConfigureAwait(false) : [];
        Tool.CatalogProvider = () => allDefs;
        return [.. allDefs.Where(Tool.IsActive), Tool.Definition];
    }

    public override async IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var innerCalls = new List<ToolCall>();
        foreach (var call in calls)
        {
            if (string.Equals(call.Name, Tool.Definition.Name, StringComparison.OrdinalIgnoreCase))
            {
                var (args, _) = call.ParseArguments();
                yield return new MessageStart(Role.Tool, Id: call.Id);
                var contents = new List<IToolResultContent>();
                await foreach (var evt in Tool.InvokeStreamingAsync(args ?? [], ct).ConfigureAwait(false))
                {
                    if (evt is IToolResultContent trc) contents.Add(trc);
                    else if (evt is Text t) contents.Add(t);
                }
                yield return new MessageDelta(call.Id, Content: new ToolResult(call.Id, contents));
                yield return new MessageEnd(Id: call.Id);
            }
            else
            {
                innerCalls.Add(call);
            }
        }

        if (innerCalls.Count > 0)
        {
            await foreach (var evt in base.ExecuteAsync(innerCalls, ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
    }
}
