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

        var methodName = tool.Name;

        return $"public static object? {methodName}({string.Join(", ", paramList)}) => throw new NotImplementedException(\"Tool called via delegate\");";
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
            "array" => "List<object>",
            "object" => "Dictionary<string, object>",
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
        return "null";
    }

    public static Dictionary<string, Delegate> CreateToolDelegates(
        IReadOnlyList<Tool> tools,
        IToolExecutor executor)
    {
        var delegates = new Dictionary<string, Delegate>();

        foreach (var tool in tools)
        {
            var delegate_ = CreateToolDelegate(tool, executor);
            delegates[tool.Name] = delegate_;
        }

        return delegates;
    }

    private static Delegate CreateToolDelegate(Tool tool, IToolExecutor executor)
    {
        var method = tool.Method;
        if (method == null)
        {
            return CreateDynamicDelegate(tool, executor);
        }

        var parameters = method.GetParameters();
        var returnType = typeof(object);
        var paramTypes = new List<Type>();

        foreach (var param in parameters)
        {
            paramTypes.Add(param.ParameterType);
        }

        return CreateAsyncDelegate(tool.Name, paramTypes.ToArray(), executor);
    }

    private static Delegate CreateDynamicDelegate(Tool tool, IToolExecutor executor)
    {
        return (Func<Task<object?>>)(async () =>
        {
            var result = await executor.HandleToolCallAsync(
                new ToolCall(Guid.NewGuid().ToString("N"), tool.Name, new JsonObject()),
                default);
            return result.Result?.ForLlm();
        });
    }

    private static Delegate CreateAsyncDelegate(string toolName, Type[] paramTypes, IToolExecutor executor)
    {
        return (Func<object?[], Task<object?>>)(async (args) =>
        {
            var jsonArgs = new JsonObject();
            var paramInfos = executor.GetType().GetMethod("HandleToolCallAsync")?.GetParameters();
            
            if (paramInfos != null)
            {
                for (int i = 0; i < paramInfos.Length && i < args.Length; i++)
                {
                    jsonArgs[paramInfos[i].Name ?? $"arg{i}"] = JsonValue.Create(args[i]);
                }
            }

            var toolCall = new ToolCall(Guid.NewGuid().ToString("N"), toolName, jsonArgs);
            var result = await executor.HandleToolCallAsync(toolCall, default);
            return result.Result?.ForLlm();
        });
    }
}
