using AgentCore;
using AgentCore.MCP.Client;
using AgentCore.Tooling;
using System.Reflection;

namespace AgentCore.MCP;

public static class McpBuilderExtensions
{
    public static AgentBuilder WithMcpTools(this AgentBuilder builder, McpToolSource toolSource)
    {
        return builder.WithTools(registry => toolSource.RegisterTools(registry));
    }

    public static AgentBuilder WithMcpTools(this AgentBuilder builder, IEnumerable<McpToolSource> toolSources)
    {
        foreach (var source in toolSources)
        {
            builder.WithMcpTools(source);
        }
        return builder;
    }
}
