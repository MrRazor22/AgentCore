using AgentCore;
using AgentCore.Tool;
using ModelContextProtocol.Client;

namespace AgentCore.MCP;

public static class McpToolExtensions
{ 
    public static async Task<Toolbox> AddMcpToolsAsync(this Toolbox toolbox, McpClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(client);

        var tools = await client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
        var mcpTools = tools.Select(t => (ITool)new McpTool(client, t.ProtocolTool));
        return toolbox.AddTool(mcpTools);
    }
} 