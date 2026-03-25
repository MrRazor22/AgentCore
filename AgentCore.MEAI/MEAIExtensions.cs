using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Tooling;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Providers.MEAI;

internal static class MEAIExtensions
{
    public static IEnumerable<ChatMessage> ToMEAIChatMessages(this IReadOnlyList<Message> messages)
    {
        foreach (var msg in messages)
        {
            var chatMsg = new ChatMessage
            {
                Role = msg.Role.ToMEAIChatRole()
            };
            
            if (msg.Content is Text t)
            {
                chatMsg.Contents.Add(new TextContent(t.Value));
            }
            else if (msg.Content is ToolCall tc)
            {
                chatMsg.Contents.Add(new FunctionCallContent(tc.Id, tc.Name, tc.Arguments.ToDictionary()));
            }
            else if (msg.Content is ToolResult tr)
            {
                chatMsg.Contents.Add(new FunctionResultContent(tr.CallId, tr.Result?.AsJsonString() ?? "{}"));
            }
            
            yield return chatMsg;
        }
    }

    private static IDictionary<string, object?>? ToDictionary(this JsonObject? json)
    {
        if (json == null) return null;
        var dict = new Dictionary<string, object?>();
        foreach (var prop in json)
        {
            dict[prop.Key] = prop.Value?.ToString(); // Simple string mapping for now, MEAI often accepts this or requires complex parsing
        }
        return dict;
    }

    public static ChatRole ToMEAIChatRole(this Role role) => role switch
    {
        Role.System => ChatRole.System,
        Role.Assistant => ChatRole.Assistant,
        Role.User => ChatRole.User,
        Role.Tool => ChatRole.Tool,
        _ => ChatRole.User
    };

    public static ChatOptions ToMEAIChatOptions(this LLMOptions options)
    {
        var opts = new ChatOptions
        {
            ModelId = options.Model,
            Temperature = options.Temperature,
            TopP = options.TopP,
            MaxOutputTokens = options.MaxOutputTokens,
            FrequencyPenalty = options.FrequencyPenalty,
            PresencePenalty = options.PresencePenalty,
            ToolMode = options.ToolCallMode.ToMEAIChatToolMode()
        };

        if (options.StopSequences != null)
        {
            opts.StopSequences = options.StopSequences.ToList();
        }

        if (options.Seed.HasValue) opts.Seed = options.Seed.Value;

        if (options.ResponseSchema != null)
        {
            opts.ResponseFormat = ChatResponseFormat.Json;
        }

        return opts;
    }

    public static ChatToolMode ToMEAIChatToolMode(this ToolCallMode mode) => mode switch
    {
        ToolCallMode.None => ChatToolMode.None,
        ToolCallMode.Auto => ChatToolMode.Auto,
        ToolCallMode.Required => ChatToolMode.RequireAny,
        _ => ChatToolMode.Auto
    };

    public static IEnumerable<AITool> ToMEAITools(this IEnumerable<Tool> tools)
    {
        foreach (var tool in tools)
        {
            yield return AIFunctionFactory.Create(
                (string args) => args, 
                name: tool.Name, 
                description: tool.Description);
        }
    }

    public static AgentCore.LLM.FinishReason ToCoreFinishReason(this ChatFinishReason? reason)
    {
        if (reason == ChatFinishReason.Stop) return AgentCore.LLM.FinishReason.Stop;
        if (reason == ChatFinishReason.ToolCalls) return AgentCore.LLM.FinishReason.ToolCall;
        if (reason == ChatFinishReason.Length) return AgentCore.LLM.FinishReason.Stop;
        if (reason == ChatFinishReason.ContentFilter) return AgentCore.LLM.FinishReason.Stop;
        return AgentCore.LLM.FinishReason.Stop;
    }
    
    public static string AsJsonString(this IContent content)
    {
        if (content is Text t) return t.Value;
        if (content is ToolCall tc) return tc.Arguments.ToJsonString();
        return content.ForLlm();
    }
}
