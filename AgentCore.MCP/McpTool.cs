using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentCore.Tools;
using ProtocolTool = ModelContextProtocol.Protocol.Tool;

namespace AgentCore.MCP;

public sealed class McpTool(McpClient client, ProtocolTool tool) : ITool
{
    private readonly McpClient _client = client ?? throw new ArgumentNullException(nameof(client));  
    private static JsonSchema ParseSchema(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
            ? new JsonSchema((JsonNode.Parse(element.GetRawText()) as JsonObject) ?? new JsonObject())
            : new JsonSchema(new JsonObject());
    public ToolDefinition Definition { get; } = new(tool.Name, tool.Description ?? tool.Name, ParseSchema(tool.InputSchema));

    public async Task<IReadOnlyList<IContent>> InvokeAsync(JsonObject arguments, CancellationToken ct = default)
    {
        var dict = arguments?.Count > 0
            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments.ToJsonString())
            : null;

        var result = await _client.CallToolAsync(Definition.Name, dict, cancellationToken: ct).ConfigureAwait(false);

        if (result.IsError == true)
        {
            var msg = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text));
            throw new InvalidOperationException($"MCP tool '{Definition.Name}' failed: {msg}");
        }

        return result.Content.Select<ContentBlock, IContent>(b => b switch
        {
            TextContentBlock tb => new Text(tb.Text),
            ImageContentBlock ib => new Image(Data: ib.Data, MediaType: ib.MimeType),
            _ => new Text(b.ToString() ?? string.Empty)
        }).ToList();
    }
}
