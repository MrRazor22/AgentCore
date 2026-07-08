using AgentCore.Conversation;
using AgentCore.Tooling;
using ModelContextProtocol.Client;

namespace AgentCore.MCP.Client;

/// <summary>
/// Extension to bridge MCP tools (McpClientTool) into the AgentCore IToolRegistry.
/// </summary>
public static class McpRegistryExtensions
{
    public static void RegisterMcpTools(
        this IToolRegistry registry,
        string serverName,
        IEnumerable<McpClientTool> mcpTools)
    {
        foreach (var mcpTool in mcpTools)
        {
            var toolName = $"{serverName}.{mcpTool.ProtocolTool.Name}";

            var tool = new AgentCore.Tooling.Tool
            {
                Name = toolName,
                Description = mcpTool.ProtocolTool.Description ?? toolName,
                ParametersSchema = System.Text.Json.Nodes.JsonNode.Parse(
                    mcpTool.ProtocolTool.InputSchema.GetRawText()) as System.Text.Json.Nodes.JsonObject
                    ?? new System.Text.Json.Nodes.JsonObject(),
                Source = $"MCP.{serverName}",
                Invoker = async (args, ct) =>
                {
                    var dictArgs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(args)
                        ?? new Dictionary<string, object?>();

                    // CallAsync takes IReadOnlyDictionary<string, object?> and returns CallToolResult
                    var result = await mcpTool.CallAsync(dictArgs);

                    if (result.IsError == true)
                    {
                        var errorText = result.Content
                            .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
                            .FirstOrDefault()?.Text ?? "Unknown MCP error";
                        return new Text($"Error from MCP tool '{toolName}': {errorText}");
                    }

                    var textBlocks = result.Content
                        .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
                        .Select(t => t.Text);
                    return new Text(string.Join("\n", textBlocks));
                }
            };

            registry.Register(tool);
        }
    }
}
