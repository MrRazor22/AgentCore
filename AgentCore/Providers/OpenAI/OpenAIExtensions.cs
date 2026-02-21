using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM.Protocol;
using AgentCore.Tools;
using OpenAI.Chat;

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
            BinaryData.FromString(tool.ParametersSchema?.ToString(Newtonsoft.Json.Formatting.None) ?? "{\"type\":\"object\"}")
        )).ToList();

    public static IEnumerable<ChatMessage> ToChatMessages(this Conversation history)
    {
        foreach (var msg in history)
        {
            switch (msg.Role)
            {
                case Role.System when msg.Content is TextContent sysText:
                    yield return ChatMessage.CreateSystemMessage(sysText.Text);
                    break;

                case Role.User when msg.Content is TextContent userText:
                    yield return ChatMessage.CreateUserMessage(userText.Text);
                    break;

                case Role.Assistant when msg.Content is TextContent assistantText:
                    yield return ChatMessage.CreateAssistantMessage(assistantText.Text);
                    break;

                case Role.Assistant when msg.Content is ToolCall call:
                    yield return ChatMessage.CreateAssistantMessage(
                        toolCalls: [ChatToolCall.CreateFunctionToolCall(
                            id: call.Id,
                            functionName: call.Name,
                            functionArguments: BinaryData.FromString(call.Arguments?.ToString() ?? "{}"))]);
                    break;

                case Role.Tool when msg.Content is ToolCallResult result:
                    var payload = result.Result == null ? "{}" : result.Result.AsJsonString();
                    yield return ChatMessage.CreateToolMessage(result.Call.Id, payload);
                    break;

                default:
                    throw new InvalidOperationException($"Invalid message state for role {msg.Role} with content {msg.Content?.GetType().Name}");
            }
        }
    }

    public static void ApplySamplingOptions(this ChatCompletionOptions opts, LLMRequest? request)
    {
        var s = request?.Options;
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

        if (s.LogitBias is { Count: > 0 })
            foreach (var kvp in s.LogitBias)
                opts.LogitBiases[kvp.Key] = kvp.Value;
    }
}
