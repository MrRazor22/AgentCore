using AgentCore.LLM.Chat;
using AgentCore.Tools;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TensorSharp.Models;
using TensorSharp.Runtime;

namespace AgentCore.LLM.TensorSharp;

internal static class TensorSharpAdapterExtensions
{
    public static ChatMessage ToTensorSharpMessage(this Message msg) => new()
    {
        Role = msg.Role switch
        {
            Role.System => "system",
            Role.User => "user",
            Role.Assistant => "assistant",
            Role.Tool => "tool",
            _ => "user"
        },
        Content = string.Join("\n", msg.Contents.Select(c => c switch
        {
            Text t => t.Value,
            _ => c.ToString() ?? string.Empty
        }))
    };

    public static List<ToolFunction> ToTensorSharpTools(this IReadOnlyList<ToolDefinition> tools) =>
    tools.Select(t =>
    {
        var fn = new ToolFunction { Name = t.Name, Description = t.Description };
        if (t.ParametersSchema?.ToJsonNode() is JsonObject schema)
        {
            if (schema["properties"] is JsonObject props)
            {
                foreach (var (propName, propVal) in props)
                {
                    fn.Parameters[propName] = new ToolParameter
                    {
                        Type = propVal?["type"]?.ToString() ?? "string",
                        Description = propVal?["description"]?.ToString() ?? string.Empty
                    };
                }
            }
            if (schema["required"] is JsonArray req)
            {
                foreach (var item in req)
                    if (item?.ToString() is { } name) fn.Required.Add(name);
            }
        }
        return fn;
    }).ToList();
}
