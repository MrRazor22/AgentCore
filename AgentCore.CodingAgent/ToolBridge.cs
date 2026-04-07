using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentCore.Conversation;
using AgentCore.Tooling;
using Microsoft.CodeAnalysis;

namespace AgentCore.CodingAgent;

public static class ToolBridge
{
    public static string GenerateToolPrompt(IReadOnlyList<Tool> tools)
    {
        if (tools.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("On top of performing computations in the C# code snippets that you create, you have access to these tools, behaving like regular C# methods:");

        foreach (var tool in tools)
        {
            sb.AppendLine($"/// <summary>{tool.Description}</summary>");
            sb.AppendLine(GenerateMethodSignature(tool));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string GenerateMethodSignature(Tool tool)
    {
        var paramList = GetParametersStrings(tool);
        var methodName = GetCSharpMethodName(tool.Name);
        return $"public static object? {methodName}({string.Join(", ", paramList)}) => throw new NotImplementedException();";
    }

    public static string GenerateToolWrappers(IReadOnlyList<Tool> tools)
    {
        var sb = new StringBuilder();
        foreach (var tool in tools)
        {
            sb.AppendLine(GenerateToolWrapper(tool));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string GenerateToolWrapper(Tool tool)
    {
        var methodName = GetCSharpMethodName(tool.Name);
        var originalName = tool.Name;
        var paramList = GetParametersStrings(tool);
        
        var sb = new StringBuilder();
        sb.AppendLine($"public object? {methodName}({string.Join(", ", paramList)})");
        sb.AppendLine("{");
        sb.AppendLine("    var __args = new System.Text.Json.Nodes.JsonObject();");

        var props = tool.ParametersSchema["properties"] as JsonObject;
        if (props != null)
        {
            foreach (var (paramName, _) in props)
            {
                sb.AppendLine($"    __args[\"{paramName}\"] = System.Text.Json.Nodes.JsonValue.Create({paramName});");
            }
        }

        sb.AppendLine($"    var __call = new AgentCore.Conversation.ToolCall(System.Guid.NewGuid().ToString(\"N\"), \"{originalName}\", __args);");
        sb.AppendLine("    var __result = ToolsExecutor.HandleToolCallAsync(__call, default).GetAwaiter().GetResult();");
        sb.AppendLine("    return __result.Result?.ForLlm();");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GetCSharpMethodName(string toolName)
    {
        return toolName.Replace(".", "_").Replace("-", "_");
    }

    private static List<string> GetParametersStrings(Tool tool)
    {
        var paramList = new List<string>();
        var props = tool.ParametersSchema["properties"] as JsonObject;
        if (props != null)
        {
            foreach (var (paramName, paramNode) in props)
            {
                if (paramNode is not JsonObject paramSchema) continue;
                var type = MapJsonToCSharpType(paramSchema);
                var isOptional = paramSchema.ContainsKey("default") || (tool.ParametersSchema["required"] is JsonArray req && !req.Select(r => r?.GetValue<string>()).Contains(paramName));
                var defaultValue = isOptional ? " = " + GetDefaultValue(paramSchema) : "";
                paramList.Add($"{type} {paramName}{defaultValue}");
            }
        }
        return paramList;
    }

    private static string MapJsonToCSharpType(JsonObject schema)
    {
        if (schema.TryGetPropertyValue("type", out var typeNode))
        {
            var type = typeNode?.GetValue<string>();
            return MapToCSharpType(type ?? "object");
        }

        if (schema.ContainsKey("enum"))
        {
            return "string";
        }

        return "object";
    }

    private static string MapToCSharpType(string jsonType)
    {
        return jsonType.ToLowerInvariant() switch
        {
            "string" => "string",
            "integer" => "int",
            "number" => "double",
            "boolean" => "bool",
            "array" => "System.Collections.Generic.List<object>",
            "object" => "System.Collections.Generic.Dictionary<string, object>",
            _ => "object"
        };
    }

    private static string GetDefaultValue(JsonObject schema)
    {
        if (schema.TryGetPropertyValue("default", out var defaultNode) && defaultNode != null)
        {
            if (defaultNode is JsonValue val)
            {
                var kind = val.GetValue<JsonElement>().ValueKind;
                return kind switch
                {
                    JsonValueKind.String => $"\"{val.GetValue<string>()}\"",
                    JsonValueKind.Number => val.ToString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => "null",
                    _ => "null"
                };
            }
        }
        return "default";
    }
}
