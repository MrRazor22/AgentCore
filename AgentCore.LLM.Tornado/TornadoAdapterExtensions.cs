using System.Text.Json;
using AgentCore.LLM.Chat;
using AgentCore.Tools;
using LlmTornado.Chat;
using LlmTornado.ChatFunctions;
using LlmTornado.Code;
using LlmTornado.Common;
using ToolCall = AgentCore.LLM.Chat.ToolCall;

namespace AgentCore.LLM.Tornado;

public static class TornadoAdapterExtensions
{
    public static ChatMessageRoles ToTornadoRole(this Role role) => role switch
    {
        Role.System => ChatMessageRoles.System,
        Role.User => ChatMessageRoles.User,
        Role.Assistant => ChatMessageRoles.Assistant,
        Role.Tool => ChatMessageRoles.Tool,
        _ => throw new ArgumentOutOfRangeException(nameof(role), $"Unsupported role: {role}")
    };

    public static ChatMessage ToTornadoMessage(this Message message)
    {
        var role = message.Role.ToTornadoRole();
        var tornadoMsg = new ChatMessage(role);

        var textParts = new List<string>();
        List<LlmTornado.ChatFunctions.ToolCall>? toolCalls = null;

        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case Text text:
                    textParts.Add(text.Value);
                    break;

                case Reasoning reasoning:
                    tornadoMsg.Reasoning = reasoning.Thought;
                    break;

                case ToolCall tc:
                    toolCalls ??= [];
                    var argsStr = tc.Arguments?.ToJsonString() ?? "{}";
                    toolCalls.Add(new LlmTornado.ChatFunctions.ToolCall
                    {
                        Id = tc.Id,
                        FunctionCall = new FunctionCall
                        {
                            Name = tc.Name,
                            Arguments = argsStr
                        }
                    });
                    break;

                case ToolResult tr:
                    tornadoMsg.ToolCallId = tr.CallId;
                    textParts.Add(tr.Result?.ForLlm() ?? "");
                    break;
            }
        }

        if (textParts.Count > 0)
        {
            tornadoMsg.Content = string.Join("\n", textParts);
        }

        if (toolCalls is { Count: > 0 })
        {
            tornadoMsg.ToolCalls = toolCalls;
        }

        return tornadoMsg;
    }

    public static LlmTornado.Common.Tool ToTornadoTool(this ToolDefinition tool)
    {
        var jsonElem = tool.ParametersSchema.ToJsonElement();
        var fn = new ToolFunction(tool.Name, tool.Description, jsonElem);
        return new LlmTornado.Common.Tool(fn);
    }
}
