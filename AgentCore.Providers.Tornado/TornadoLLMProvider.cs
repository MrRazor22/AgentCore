using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Tokens;
using AgentCore.Tooling;
using LlmTornado;
using LlmTornado.Chat;
using LlmTornado.Code;
using LlmTornado.Chat.Models;
using LlmTornado.Models;
using LlmTornado.Common;
using System.Linq;
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
        IReadOnlyList<AgentCore.Tooling.Tool>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new ChatRequest
        {
            Model = _model,
        };

        if (tools is { Count: > 0 })
        {
            request.Tools = tools.Select(t =>
            {
                var jsonElement = System.Text.Json.JsonSerializer.SerializeToElement(t.ParametersSchema);
                return new LlmTornado.Common.Tool(new ToolFunction(t.Name, t.Description, jsonElement));
            }).ToList();
        }

        if (options.Temperature.HasValue) request.Temperature = options.Temperature.Value;
        if (options.TopP.HasValue) request.TopP = options.TopP.Value;
        if (options.MaxOutputTokens.HasValue) request.MaxTokens = options.MaxOutputTokens.Value;

        if (options.ReasoningEffort.HasValue)
        {
            request.ReasoningEffort = options.ReasoningEffort.Value switch
            {
                ReasoningEffort.None => LlmTornado.Code.ChatReasoningEfforts.None,
                ReasoningEffort.Low => LlmTornado.Code.ChatReasoningEfforts.Low,
                ReasoningEffort.Medium => LlmTornado.Code.ChatReasoningEfforts.Medium,
                ReasoningEffort.High => LlmTornado.Code.ChatReasoningEfforts.High,
                _ => null
            };
        }
        request.ReasoningFormat = LlmTornado.Code.ChatReasoningFormats.Parsed;

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
                    ReasoningTokenHandler = (reasoning) =>
                    {
                        if (reasoning.Content is not null)
                        {
                            channel.Writer.TryWrite(new ReasoningDelta(reasoning.Content));
                        }
                        return ValueTask.CompletedTask;
                    },
                    MessagePartHandler = (part) =>
                    {
                        if (part.Reasoning is not null && part.Reasoning.Content is not null)
                        {
                            channel.Writer.TryWrite(new ReasoningDelta(part.Reasoning.Content));
                        }
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
                              var argsStr = call.Arguments;
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
                var translated = ex;
                if (IsContextLimitError(ex))
                {
                    translated = new ContextLengthExceededException();
                }
                else if (IsTransientError(ex))
                {
                    translated = new RetryableException(ex.Message, ex);
                }

                channel.Writer.TryComplete(translated);
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

    private static bool IsContextLimitError(Exception ex)
    {
        var msg = ex.ToString();
        return msg.Contains("context_length_exceeded", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("maximum context length", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("too many tokens", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("token limit exceeded", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("context window limit", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransientError(Exception ex)
    {
        if (ex is System.TimeoutException || 
            ex is System.IO.IOException || 
            ex is System.Net.Sockets.SocketException)
        {
            return true;
        }

        if (ex is System.Net.Http.HttpRequestException httpEx)
        {
            if (httpEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                httpEx.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                httpEx.StatusCode == System.Net.HttpStatusCode.GatewayTimeout ||
                httpEx.StatusCode == System.Net.HttpStatusCode.BadGateway ||
                httpEx.StatusCode == System.Net.HttpStatusCode.InternalServerError)
            {
                return true;
            }
        }

        var msg = ex.ToString();
        return msg.Contains("429", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("503", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("504", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("502", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("500", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("HttpRequestException", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("SocketException", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("TimeoutException", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("overloaded", StringComparison.OrdinalIgnoreCase);
    }
}

