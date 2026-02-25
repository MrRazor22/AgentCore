using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM.Protocol;
using Google.GenAI.Types;
using Tool = AgentCore.Tools.Tool;
using CoreFinishReason = AgentCore.LLM.Protocol.FinishReason;

namespace AgentCore.Providers.Gemini;

public static class GeminiExtensions
{
    public static CoreFinishReason ToFinishReason(this string? reason) => reason switch
    {
        "STOP" => CoreFinishReason.Stop,
        "TOOL_CODE" => CoreFinishReason.ToolCall,
        _ => CoreFinishReason.Stop
    };

    public static List<Content> ToGeminiContents(this IList<Message> history)
    {
        var contents = new List<Content>();
        var toolCallLookup = history
            .Where(m => m.Role == Role.Assistant && m.Content is ToolCall tc)
            .ToDictionary(m => ((ToolCall)m.Content!).Id, m => (ToolCall)m.Content!);

        foreach (var msg in history)
        {
            var content = msg.ToGeminiContent(toolCallLookup);
            if (content != null)
                contents.Add(content);
        }

        return contents;
    }

    private static Content? ToGeminiContent(this Message msg, Dictionary<string, ToolCall>? toolCallLookup = null)
    {
        return msg switch
        {
            { Role: Role.System, Content: Text text } => new Content
            {
                Role = "user",
                Parts = [new Part { Text = text.Value }]
            },
            { Role: Role.User, Content: Text text } => new Content
            {
                Role = "user",
                Parts = [new Part { Text = text.Value }]
            },
            { Role: Role.Assistant, Content: Text text } => new Content
            {
                Role = "model",
                Parts = [new Part { Text = text.Value }]
            },
            { Role: Role.Assistant, Content: ToolCall call } => new Content
            {
                Role = "model",
                Parts = [new Part
                {
                    FunctionCall = new FunctionCall
                    {
                        Name = call.Name,
                        Args = call.Arguments?.ToJsonString() != "{}"
                            ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(call.Arguments?.ToJsonString() ?? "{}")
                            : new Dictionary<string, object?>()
                    }
                }]
            },
            { Role: Role.Tool, Content: ToolResult result } => new Content
            {
                Role = "user",
                Parts = [new Part
                {
                    FunctionResponse = new FunctionResponse
                    {
                        Name = toolCallLookup?.GetValueOrDefault(result.CallId)?.Name ?? "",
                        Response = new Dictionary<string, object?> { { "result", result.Result?.AsJsonString() ?? "{}" } }
                    }
                }]
            },
            _ => null
        };
    }

    public static List<FunctionDeclaration> ToGeminiTools(this IEnumerable<Tool> tools)
    {
        return tools.Select(tool => new FunctionDeclaration
        {
            Name = tool.Name,
            Description = tool.Description ?? "",
            Parameters = tool.ParametersSchema?.ToJsonSchema()
        }).ToList();
    }

    public static Schema ToJsonSchema(this System.Text.Json.Nodes.JsonObject obj)
    {
        var schema = new Schema { Type = TypeEnum.OBJECT };

        var props = new Dictionary<string, Schema>();
        if (obj.TryGetPropertyValue("properties", out var properties) && properties is System.Text.Json.Nodes.JsonObject propsObj)
        {
            foreach (var prop in propsObj)
            {
                if (prop.Value is System.Text.Json.Nodes.JsonObject propSchema)
                {
                    props[prop.Key] = ParsePropertySchema(propSchema);
                }
            }
        }
        schema.Properties = props;

        if (obj.TryGetPropertyValue("required", out var required) && required is System.Text.Json.Nodes.JsonArray requiredArray)
        {
            schema.Required = requiredArray.Select(r => r?.ToString()).Where(r => r != null).ToList()!;
        }

        return schema;
    }

    private static Schema ParsePropertySchema(System.Text.Json.Nodes.JsonObject obj)
    {
        var schema = new Schema();

        if (obj.TryGetPropertyValue("type", out var typeNode))
        {
            var typeStr = typeNode?.ToString()?.ToLower();
            schema.Type = typeStr switch
            {
                "string" => TypeEnum.STRING,
                "integer" => TypeEnum.INTEGER,
                "number" => TypeEnum.NUMBER,
                "boolean" => TypeEnum.BOOLEAN,
                "array" => TypeEnum.ARRAY,
                "object" => TypeEnum.OBJECT,
                _ => TypeEnum.STRING
            };
        }

        if (obj.TryGetPropertyValue("description", out var desc))
        {
            schema.Description = desc?.ToString();
        }

        if (obj.TryGetPropertyValue("items", out var items) && items is System.Text.Json.Nodes.JsonObject itemsObj)
        {
            schema.Items = ParsePropertySchema(itemsObj);
        }

        return schema;
    }

    public static GenerateContentConfig ToGeminiConfig(this LLMRequest request)
    {
        var config = new GenerateContentConfig();

        if (request.Options != null)
        {
            var opts = request.Options;
            if (opts.Temperature.HasValue) config.Temperature = opts.Temperature.Value;
            if (opts.TopP.HasValue) config.TopP = opts.TopP.Value;
            if (opts.TopK.HasValue) config.TopK = opts.TopK.Value;
            if (opts.MaxOutputTokens.HasValue) config.MaxOutputTokens = opts.MaxOutputTokens.Value;
            if (opts.StopSequences is { Count: > 0 }) config.StopSequences = opts.StopSequences.ToList();
        }

        if (request.OutputType != null)
        {
            config.ResponseMimeType = "application/json";
            config.ResponseSchema = request.OutputType.GetSchemaForType().ToJsonSchema();
        }

        if (request.AvailableTools != null)
        {
            config.Tools = [new Google.GenAI.Types.Tool
            {
                FunctionDeclarations = request.AvailableTools.ToGeminiTools()
            }];
        }

        config.ToolConfig = request.ToolCallMode switch
        {
            ToolCallMode.None => new ToolConfig { FunctionCallingConfig = new FunctionCallingConfig { Mode = "NONE" } },
            ToolCallMode.Required => new ToolConfig { FunctionCallingConfig = new FunctionCallingConfig { Mode = "ANY" } },
            _ => new ToolConfig { FunctionCallingConfig = new FunctionCallingConfig { Mode = "AUTO" } }
        };

        return config;
    }

    private static class TypeEnum
    {
        public const string STRING = "STRING";
        public const string INTEGER = "INTEGER";
        public const string NUMBER = "NUMBER";
        public const string BOOLEAN = "BOOLEAN";
        public const string ARRAY = "ARRAY";
        public const string OBJECT = "OBJECT";
    }
}
