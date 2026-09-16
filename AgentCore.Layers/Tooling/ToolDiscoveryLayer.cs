using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using System.Runtime.CompilerServices;
using System.Text;

namespace AgentCore.Tooling;

public sealed class ToolDiscoveryLayer : ToolingLayer
{
    private readonly HashSet<string> _coreTools;
    private readonly HashSet<string> _activeTools = new(StringComparer.OrdinalIgnoreCase);
    private readonly IToolSearcher _searcher;
    private readonly ToolDefinition _discoveryTool;

    public ToolDiscoveryLayer(
        IEnumerable<string>? coreTools = null,
        IToolSearcher? searcher = null,
        string discoveryToolName = "search_tools",
        string discoveryToolDescription = "Search available tools in the catalog and activate them for the session.")
    {
        _coreTools = new(coreTools ?? [], StringComparer.OrdinalIgnoreCase);
        _searcher = searcher ?? new KeywordToolSearcher();
        _discoveryTool = new(
            discoveryToolName,
            discoveryToolDescription,
            new JsonSchemaBuilder()
                .Type("object")
                .AddProperty("query", new JsonSchemaBuilder()
                    .Type("string")
                    .Description("Keywords or intent to search for available tools.")
                    .Build())
                .Build());
    }

    public override IReadOnlyList<ToolDefinition> GetDefinitions()
    {
        var catalog = base.GetDefinitions();
        var definitions = new List<ToolDefinition> { _discoveryTool };

        foreach (var def in catalog)
        {
            if (_coreTools.Contains(def.Name) || _activeTools.Contains(def.Name))
                definitions.Add(def);
        }

        return definitions;
    }

    public override async IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var passthrough = new List<ToolCall>();

        foreach (var call in calls)
        {
            if (string.Equals(call.Name, _discoveryTool.Name, StringComparison.OrdinalIgnoreCase))
            {
                yield return new MessageStart(Role.Tool, Id: call.Id);
                yield return new MessageDelta(call.Id, Metadata: new ToolCallId(call.Id));
                yield return new MessageDelta(call.Id, Content: new Text(ExecuteDiscovery(call)));
                yield return new MessageEnd(Id: call.Id);
            }
            else
            {
                passthrough.Add(call);
            }
        }

        if (passthrough.Count > 0)
        {
            await foreach (var evt in base.ExecuteAsync(passthrough, ct).ConfigureAwait(false))
                yield return evt;
        }
    }

    private string ExecuteDiscovery(ToolCall call)
    {
        var (args, parseError) = call.ParseArguments();
        if (parseError != null) return parseError;

        var query = args?["query"]?.ToString();
        if (string.IsNullOrWhiteSpace(query))
            return "No search query provided. Please provide keywords to search for tools.";

        var catalog = base.GetDefinitions();
        var matches = _searcher.Search(query, catalog);

        if (matches.Count == 0)
            return $"No tools found matching '{query}'.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found and activated {matches.Count} tool(s):");
        foreach (var tool in matches)
        {
            _activeTools.Add(tool.Name);
            sb.AppendLine($"- {tool.Name}: {tool.Description}");
        }
        sb.Append("You may now invoke these tools directly.");
        return sb.ToString();
    }
}
