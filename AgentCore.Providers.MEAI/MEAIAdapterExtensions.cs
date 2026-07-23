using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using Microsoft.Extensions.AI;

namespace AgentCore.LLM.MEAI;

internal static class MEAIAdapterExtensions
{
    public static ChatRole ToMEAIRole(this Role role) => role switch
    {
        Role.System => ChatRole.System,
        Role.User => ChatRole.User,
        Role.Assistant => ChatRole.Assistant,
        Role.Tool => ChatRole.Tool,
        _ => throw new ArgumentOutOfRangeException(nameof(role), $"Unsupported role: {role}")
    };

    public static ChatMessage ToMEAIMessage(this Message message)
    {
        var role = message.Role.ToMEAIRole();
        var contents = new List<AIContent>();

        foreach (var content in message.Contents)
        {
            if (content is Text text)
            {
                contents.Add(new TextContent(text.Value));
            }
            else if (content is Reasoning reasoning)
            {
                contents.Add(new TextReasoningContent(reasoning.Thought));
            }
            else if (content is ToolCall tc)
            {
                IDictionary<string, object?>? argsDict = null;
                if (tc.Arguments != null)
                {
                    try
                    {
                        var argsJson = tc.Arguments.ToJsonString();
                        argsDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(argsJson);
                    }
                    catch
                    {
                        // Fallback in case of parse issues
                    }
                }
                contents.Add(new FunctionCallContent(tc.Id, tc.Name, argsDict));
            }
            else if (content is ToolResult tr)
            {
                contents.Add(new FunctionResultContent(tr.CallId, tr.Result?.ForLlm()));
            }
        }

        return new ChatMessage(role, contents);
    }
}
