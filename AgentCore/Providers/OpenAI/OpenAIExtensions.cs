using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM.Protocol;
using AgentCore.Tools;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AgentCore.Providers.OpenAI
{
    public static class OpenAIExtensions
    {
        public static FinishReason ToChatFinishReason(this ChatFinishReason reason)
        {
            return reason switch
            {
                ChatFinishReason.Stop => FinishReason.Stop,
                ChatFinishReason.ToolCalls => FinishReason.ToolCall,
                _ => FinishReason.Stop
            };
        }

        public static ChatToolChoice ToChatToolChoice(this ToolCallMode mode)
        {
            return mode switch
            {
                ToolCallMode.None => ChatToolChoice.CreateNoneChoice(),
                ToolCallMode.Required => ChatToolChoice.CreateRequiredChoice(),
                _ => ChatToolChoice.CreateAutoChoice()
            };
        }
        // Converts your Tool collection to OpenAI ChatTools (functions)
        public static List<ChatTool> ToChatTools(this IEnumerable<Tool> tools)
        {
            return tools.Select(tool =>
                ChatTool.CreateFunctionTool(
                    tool.Name,
                    tool.Description ?? "",
                    BinaryData.FromString(
                        tool.ParametersSchema?.ToString(Newtonsoft.Json.Formatting.None)
                        ?? "{\"type\":\"object\"}"
                    )
                )
            ).ToList();
        }

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
                            toolCalls: new[]
                            {
                        ChatToolCall.CreateFunctionToolCall(
                            id: call.Id,
                            functionName: call.Name,
                            functionArguments: BinaryData.FromString(
                                call.Arguments?.ToString() ?? "{}"))
                            });
                        break;

                    case Role.Tool when msg.Content is ToolCallResult result:
                        var payload = result.Result == null
                            ? "{}"
                            : result.Result.AsJsonString(); // keep real output if exists

                        yield return ChatMessage.CreateToolMessage(
                            result.Call.Id,
                            payload);
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Invalid message state for role {msg.Role} with content {msg.Content?.GetType().Name}");
                }
            }
        }

        public static void ApplySamplingOptions(
            this ChatCompletionOptions opts,
            LLMRequestBase? request)
        {
            var s = request?.Options;
            if (s == null)
                return;

            // --- Temperature ---
            if (s.Temperature.HasValue)
                opts.Temperature = s.Temperature.Value;

            // --- TopP ---
            if (s.TopP.HasValue)
                opts.TopP = s.TopP.Value;

            // --- TopK ---
            //if (s.TopK.HasValue)
            // OpenAI doesn't support TopK sampling.
            // Do nothing.

            // --- Max tokens ---
            if (s.MaxOutputTokens.HasValue)
                opts.MaxOutputTokenCount = s.MaxOutputTokens.Value;

            // --- Seed ---
#pragma warning disable OPENAI001
            if (s.Seed.HasValue)
                opts.Seed = s.Seed.Value;
#pragma warning restore OPENAI001

            // --- Frequency penalty ---
            if (s.FrequencyPenalty.HasValue)
                opts.FrequencyPenalty = s.FrequencyPenalty.Value;

            // --- Presence penalty ---
            if (s.PresencePenalty.HasValue)
                opts.PresencePenalty = s.PresencePenalty.Value;

            // --- Stop sequences ---
            if (s.StopSequences != null && s.StopSequences.Count > 0)
            {
                foreach (var stop in s.StopSequences)
                    opts.StopSequences.Add(stop);
            }

            // --- Logit bias ---
            if (s.LogitBias != null && s.LogitBias.Count > 0)
            {
                foreach (var kvp in s.LogitBias)
                    opts.LogitBiases[kvp.Key] = kvp.Value;
            }
        }
    }
}
