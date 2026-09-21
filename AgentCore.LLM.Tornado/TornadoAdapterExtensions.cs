using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AgentCore.LLM.Chat;
using AgentCore.Tool;
using LlmTornado.Chat;
using LlmTornado.ChatFunctions;
using LlmTornado.Code;
using LlmTornado.Common;
using LlmTornado.Images;
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

        if (message.Role == Role.Tool)
            tornadoMsg.ToolCallId = message.Contents.OfType<ToolResult>().FirstOrDefault()?.ToolCallId ?? message.Id;

        var textParts = new List<string>();
        List<ChatMessagePart>? parts = null;
        List<LlmTornado.ChatFunctions.ToolCall>? toolCalls = null;

        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case Text text: textParts.Add(text.Value); break;
                case ToolResult tr: textParts.AddRange(tr.Contents.OfType<Text>().Select(t => t.Value)); break;

                case Reasoning reasoning:
                    tornadoMsg.Reasoning = reasoning.Thought;
                    break;

                case Image img:
                    parts ??= [];
                    if (img.Uri != null)
                        parts.Add(new ChatMessagePart(img.Uri));
                    else if (img.Data != null)
                        parts.Add(new ChatMessagePart(Convert.ToBase64String(img.Data.Value.ToArray()), ImageDetail.Auto, img.MediaType));
                    break;

                case Audio audio:
                    parts ??= [];
                    var audioFormat = audio.MediaType.Contains("mp3", StringComparison.OrdinalIgnoreCase) ? ChatAudioFormats.Mp3 : ChatAudioFormats.Wav;
                    if (audio.Data != null)
                        parts.Add(new ChatMessagePart(audio.Data.Value.ToArray(), audioFormat));
                    else if (audio.Uri != null)
                        parts.Add(new ChatMessagePart(new ChatMessagePartFileLinkData(audio.Uri.AbsoluteUri, audio.MediaType)));
                    break;

                case Video video:
                    parts ??= [];
                    if (video.Uri != null)
                        parts.Add(new ChatMessagePart(new ChatMessagePartFileLinkData(video.Uri.AbsoluteUri, video.MediaType)));
                    break;

                case ToolCall tc:
                    toolCalls ??= [];
                    var argsStr = string.IsNullOrWhiteSpace(tc.Arguments) ? "{}" : tc.Arguments;
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
            }
        }

        if (parts is { Count: > 0 })
        {
            if (textParts.Count > 0)
                parts.Insert(0, new ChatMessagePart(string.Join("\n", textParts)));
            tornadoMsg.Parts = parts;
        }
        else if (textParts.Count > 0)
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
public sealed record TornadoUsage(ChatUsage Raw) : IMetadata;
