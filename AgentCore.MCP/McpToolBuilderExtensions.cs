using AgentCore;
using AgentCore.Tool;
using ModelContextProtocol.Client;

namespace AgentCore.MCP;

public static class McpToolExtensions
{ 
    public static async Task<Agent> WithMcpToolsAsync(this Agent agent, McpClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(client);

        var tools = await client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
        var mcpTools = tools.Select(t => (ITool)new McpTool(client, t.ProtocolTool)).ToArray();
        return agent.WithTools([.. agent.Tools, .. mcpTools]);
    }
} 