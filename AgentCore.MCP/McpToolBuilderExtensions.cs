using AgentCore.LLM.Chat;
using AgentCore.Tool;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;

namespace AgentCore.MCP;

public static class McpToolBuilderExtensions
{
    public static async Task<ToolingBuilder> WithMcpToolsAsync(this ToolingBuilder builder, McpClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);

        var tools = await client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
        foreach (var tool in tools)
        {
            builder.WithTools(new McpTool(client, tool.ProtocolTool));
        }

        return builder;
    }

    public static async Task<AgentBuilder> WithMcpToolsAsync(this AgentBuilder builder, McpClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);

        var tools = await client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
        return builder.UseTooling(tb =>
        {
            foreach (var tool in tools)
            {
                tb.WithTools(new McpTool(client, tool.ProtocolTool));
            }
        });
    }
}

public static class AgentMcpExtensions
{
    public static McpServerTool AsMcpTool(this Agent agent, string name, string description)
        => McpServerTool.Create(
            (string prompt, CancellationToken ct) => agent.InvokeAsync(new Text(prompt), ct),
            new() { Name = name, Description = description });
}