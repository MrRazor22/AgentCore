using AgentCore.Conversation;
using AgentCore.Json;
using AgentCore.LLM;
using AgentCore.Tooling;
using OpenAI.Chat;
using System.Text.Json.Nodes;

namespace AgentCore.Providers.OpenAI;

public static class OpenAIExtensions
{
    public static FinishReason ToChatFinishReason(this ChatFinishReason reason) => reason switch
    {
        ChatFinishReason.Stop => FinishReason.Stop,
        ChatFinishReason.ToolCalls => FinishReason.ToolCall,
        _ => FinishReason.Stop
    };

    public static ChatToolChoice ToChatToolChoice(this ToolCallMode mode) => mode switch
    {
        ToolCallMode.None => ChatToolChoice.CreateNoneChoice(),
        ToolCallMode.Required => ChatToolChoice.CreateRequiredChoice(),
        _ => ChatToolChoice.CreateAutoChoice()
    };

    public static List<ChatTool> ToChatTools(this IEnumerable<Tool> tools)
        => tools.Select(tool => ChatTool.CreateFunctionTool(
            tool.Name,
            tool.Description ?? "",
            BinaryData.FromString(tool.ParametersSchema?.ToJsonString() ?? "{\"type\":\"object\"}")
        )).ToList();

    public static IEnumerable<ChatMessage> ToChatMessages(this IReadOnlyList<Message> history)
    {
        foreach (var msg in history)
        {
            switch (msg.Role)
            {
                case Role.System:
                    var sysText = msg.Contents.OfType<Text>().FirstOrDefault();
                    var sysSummary = msg.Contents.OfType<Summary>().FirstOrDefault();
                    var sysContent = sysText?.Value ?? sysSummary?.ForLlm() ?? "";
                    if (!string.IsNullOrEmpty(sysContent))
                        yield return ChatMessage.CreateSystemMessage(sysContent);
                    break;

                case Role.User:
                    var userText = msg.Contents.OfType<Text>().FirstOrDefault();
                    if (userText != null)
                        yield return ChatMessage.CreateUserMessage(userText.Value);
                    break;

                case Role.Assistant:
                    var assistantText = msg.Contents.OfType<Text>().FirstOrDefault();
                    var reasoning = msg.Contents.OfType<Reasoning>().FirstOrDefault();
                    var toolCalls = msg.Contents.OfType<ToolCall>().ToList();

                    if (toolCalls.Count > 0)
                    {
                        yield return ChatMessage.CreateAssistantMessage(
                            toolCalls: toolCalls.Select(call => ChatToolCall.CreateFunctionToolCall(
                                id: call.Id,
                                functionName: call.Name,
                                functionArguments: BinaryData.FromString(call.Arguments?.ToJsonString() ?? "{}"))).ToList());
                    }
                    else if (assistantText != null || reasoning != null)
                    {
                        var text = assistantText?.Value ?? "";
                        yield return ChatMessage.CreateAssistantMessage(text);
                    }
                    break;

                case Role.Tool:
                    var toolResult = msg.Contents.OfType<ToolResult>().FirstOrDefault();
                    if (toolResult != null)
                    {
                        var payload = toolResult.Result == null ? "{}" : toolResult.Result.AsJsonString();
                        yield return ChatMessage.CreateToolMessage(toolResult.CallId, payload);
                    }
                    break;
            }
        }
    }

    public static void ApplySamplingOptions(this ChatCompletionOptions opts, LLMOptions? options)
    {
        var s = options;
        if (s == null) return;

        if (s.Temperature.HasValue) opts.Temperature = s.Temperature.Value;
        if (s.TopP.HasValue) opts.TopP = s.TopP.Value;
        if (s.MaxOutputTokens.HasValue) opts.MaxOutputTokenCount = s.MaxOutputTokens.Value;
#pragma warning disable OPENAI001
        if (s.Seed.HasValue) opts.Seed = s.Seed.Value;
#pragma warning restore OPENAI001
        if (s.FrequencyPenalty.HasValue) opts.FrequencyPenalty = s.FrequencyPenalty.Value;
        if (s.PresencePenalty.HasValue) opts.PresencePenalty = s.PresencePenalty.Value;

        if (s.StopSequences is { Count: > 0 })
            foreach (var stop in s.StopSequences)
                opts.StopSequences.Add(stop);
    }
}
