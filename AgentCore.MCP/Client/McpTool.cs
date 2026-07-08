using AgentCore.Conversation;
using AgentCore.Tooling;
using ModelContextProtocol.Client;
using System.Text.Json.Nodes;

namespace AgentCore.MCP.Client;

public sealed class McpTool : Tool
{
    private readonly McpClientTool _mcpTool;

    public McpTool(string serverName, McpClientTool mcpTool)
        : base(
            $"{serverName}_{mcpTool.ProtocolTool.Name}",
            mcpTool.ProtocolTool.Description ?? $"{serverName}_{mcpTool.ProtocolTool.Name}",
            new AgentCore.Json.JsonSchema(
                System.Text.Json.Nodes.JsonNode.Parse(mcpTool.ProtocolTool.InputSchema.GetRawText()) as System.Text.Json.Nodes.JsonObject
                ?? new System.Text.Json.Nodes.JsonObject()
            ),
            false,
            $"MCP.{serverName}")
    {
        _mcpTool = mcpTool;
    }

    public override async Task<object?> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var dictArgs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments)
            ?? new Dictionary<string, object?>();

        var result = await _mcpTool.CallAsync(dictArgs);

        if (result.IsError == true)
        {
            var errorText = result.Content
                .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
                .FirstOrDefault()?.Text ?? "Unknown MCP error";
            return new Text($"Error from MCP tool '{Name}': {errorText}");
        }

        var textBlocks = result.Content
            .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
            .Select(t => t.Text);
        return new Text(string.Join("\n", textBlocks));
    }
}
