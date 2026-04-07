using AgentCore.Tooling;
using ModelContextProtocol.Client;

namespace AgentCore.MCP.Client;

/// <summary>
/// Represents a live connection to an external MCP server and its available tools.
/// </summary>
public sealed class McpToolSource : IAsyncDisposable
{
    private readonly McpClient _client;
    private readonly string _serverName;
    private readonly IReadOnlyList<McpClientTool> _mcpTools;

    public string ServerName => _serverName;
    public IReadOnlyList<McpClientTool> McpTools => _mcpTools;

    private McpToolSource(McpClient client, string serverName, IList<McpClientTool> mcpTools)
    {
        _client = client;
        _serverName = serverName;
        _mcpTools = mcpTools.ToList();
    }

    /// <summary>
    /// Connects to an MCP server via the given transport and discovers its tools.
    /// </summary>
    public static async Task<McpToolSource> ConnectAsync(
        IClientTransport transport,
        string serverName,
        CancellationToken ct = default)
    {
        var client = await McpClient.CreateAsync(transport, cancellationToken: ct);

        // ListToolsAsync(RequestOptions?, CancellationToken) returns IList<McpClientTool>
        var tools = await client.ListToolsAsync(cancellationToken: ct);
        return new McpToolSource(client, serverName, tools);
    }

    /// <summary>
    /// Registers all discovered MCP tools into the AgentCore tool registry.
    /// </summary>
    public void RegisterTools(IToolRegistry registry)
        => registry.RegisterMcpTools(_serverName, _mcpTools);

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
