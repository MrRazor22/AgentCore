using AgentCore.LLM.Chat;
using AgentCore.Tooling;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;

namespace AgentCore.MCP;

public static class McpToolBuilderExtensions
{ 
    public static async Task<AgentBuilder> WithMcpToolsAsync(this AgentBuilder builder, McpClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);

        var tools = await client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
        return builder.UseToolbox(tb =>
        {
            foreach (var tool in tools)
            {
                tb.WithTools(new McpTool(client, tool.ProtocolTool));
            }
        });
    }
} 