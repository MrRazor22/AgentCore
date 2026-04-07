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
                Invoker = async (args) =>
                {
                    // Map positional AgentCore args to named MCP args using the schema property order
                    var dictArgs = new Dictionary<string, object?>();

                    var schema = mcpTool.ProtocolTool.InputSchema;
                    if (schema.TryGetProperty("properties", out var propsElement))
                    {
                        var propNames = propsElement.EnumerateObject().Select(p => p.Name).ToList();
                        for (int i = 0; i < System.Math.Min(args.Length, propNames.Count); i++)
                            dictArgs[propNames[i]] = args[i];
                    }

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
