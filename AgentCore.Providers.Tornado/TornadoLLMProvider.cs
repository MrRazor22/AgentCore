using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Tokens;
using AgentCore.Tooling;
using LlmTornado;
using LlmTornado.Chat;
using LlmTornado.Code;
using LlmTornado.Chat.Models;
using LlmTornado.Models;
using System.Runtime.CompilerServices;

namespace AgentCore.Providers.Tornado;

public class TornadoLLMProvider : ILLMProvider
{
    private readonly TornadoApi _api;
    private readonly ChatModel _model;

    public TornadoLLMProvider(TornadoApi api, ChatModel model)
    {
        _api = api;
        _model = model;
    }

    public async IAsyncEnumerable<IContentDelta> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        IReadOnlyList<Tool>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new ChatRequest
        {
            Model = _model,
        };

        if (options.Temperature.HasValue) request.Temperature = options.Temperature.Value;
        if (options.TopP.HasValue) request.TopP = options.TopP.Value;
        if (options.MaxOutputTokens.HasValue) request.MaxTokens = options.MaxOutputTokens.Value;

        var chat = _api.Chat.CreateConversation(request);

        foreach (var msg in messages)
        {
            var contentStr = string.Join("\n", msg.Contents.Select(c => c.ForLlm()));
            switch (msg.Role)
            {
                case Role.System:
                    chat.AppendMessage(new ChatMessage(ChatMessageRoles.System, contentStr));
                    break;
                case Role.User:
                    chat.AppendMessage(new ChatMessage(ChatMessageRoles.User, contentStr));
                    break;
                case Role.Assistant:
                    chat.AppendMessage(new ChatMessage(ChatMessageRoles.Assistant, contentStr));
                    break;
                case Role.Tool:
                    // Currently appending tool results as basic user text since LlmTornado 
                    // handles native tool routing differently through its conversation block.
                    chat.AppendMessage(new ChatMessage(ChatMessageRoles.User, $"[Tool Result]: {contentStr}"));
                    break;
            }
        }

        // We wrap LlmTornado's streaming callbacks into an async enumerator using a channel or just basic mapping.
        // For simplicity, we yield text and reasoning blocks as they stream.
        // Note: LlmTornado uses custom asynchronous event handlers instead of IAsyncEnumerable natively.
        
        var channel = System.Threading.Channels.Channel.CreateUnbounded<IContentDelta>();

        _ = Task.Run(async () =>
        {
            try
            {
                await chat.StreamResponseRich(new ChatStreamEventHandler
                {
                    MessagePartHandler = (part) =>
                    {
                        if (part.Text is not null)
                        {
                            channel.Writer.TryWrite(new TextDelta(part.Text));
                        }
                        return ValueTask.CompletedTask;
                    },
                    FunctionCallHandler = (calls) =>
                    {
                         // Emit tool calls
                         int idx = 0;
                         foreach(var call in calls)
                         {
                              var argsStr = System.Text.Json.JsonSerializer.Serialize(call.Arguments);
                              channel.Writer.TryWrite(new ToolCallDelta(idx++, call.ToolCall?.Id ?? Guid.NewGuid().ToString(), call.Name, argsStr));
                         }
                         return ValueTask.CompletedTask;
                    },
                    OnUsageReceived = (usage) => 
                    {
                        if (usage.TotalTokens > 0)
                        {
                            channel.Writer.TryWrite(new MetaDelta(null, new TokenUsage(usage.PromptTokens, usage.CompletionTokens, 0)));
                        }
                        return ValueTask.CompletedTask;
                    }
                }, ct);
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, ct);

        while (await channel.Reader.WaitToReadAsync(ct))
        {
            while (channel.Reader.TryRead(out var item))
            {
                yield return item;
            }
        }
    }
}
