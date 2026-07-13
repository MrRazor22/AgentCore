using AgentCore.Conversation;
using AgentCore.Schema;
using AgentCore.LLM;
using AgentCore.LLM.Exceptions;
using LlmTornado;
using LlmTornado.Chat;
using LlmTornado.Chat.Models;
using LlmTornado.Code;
using LlmTornado.Common;
using System.Runtime.CompilerServices;

namespace AgentCore.Providers.Tornado;

public class TornadoLLMProvider : ILLMProvider
{
    private readonly TornadoApi _api;
    private readonly ChatModel _model;
    private readonly LLMOptions _options;

    public TornadoLLMProvider(TornadoApi api, ChatModel model, LLMOptions? options = null)
    {
        _api = api;
        _model = model;
        _options = options ?? new LLMOptions();
    }

    public int? ContextWindow => _model.ContextTokens;

    public async IAsyncEnumerable<IContentDelta> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
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
                using var stream = new System.IO.MemoryStream();
                using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
                {
                    t.ParametersSchema.WriteTo(writer);
                }
                var jsonElement = System.Text.Json.JsonDocument.Parse(stream.ToArray()).RootElement;
                return new LlmTornado.Common.Tool(new ToolFunction(t.Name, t.Description, jsonElement));
            }).ToList();
        }

        var temp = options?.Temperature ?? _options.Temperature;
        var maxTokens = options?.MaxOutputTokens ?? _options.MaxOutputTokens;

        if (temp.HasValue) request.Temperature = temp.Value;
        if (maxTokens.HasValue) request.MaxTokens = maxTokens.Value;
        request.ReasoningFormat = LlmTornado.Code.ChatReasoningFormats.Parsed;

        var chat = _api.Chat.CreateConversation(request);

        foreach (var msg in messages)
        {
            switch (msg.Role)
            {
                case Role.System:
                    {
                        var contentStr = string.Join("\n", msg.Contents.Select(c => c.ForLlm()));
                        chat.AppendMessage(new ChatMessage(ChatMessageRoles.System, contentStr));
                    }
                    break;
                case Role.User:
                    {
                        var contentStr = string.Join("\n", msg.Contents.Select(c => c.ForLlm()));
                        chat.AppendMessage(new ChatMessage(ChatMessageRoles.User, contentStr));
                    }
                    break;
                case Role.Assistant:
                    {
                        var assistantContent = string.Join("\n", msg.Contents.Where(c => c is not AgentCore.Conversation.ToolCall).Select(c => c.ForLlm()));
                        var assistantMsg = new ChatMessage(ChatMessageRoles.Assistant)
                        {
                            Content = string.IsNullOrEmpty(assistantContent) ? null : assistantContent
                        };
                        var toolCalls = msg.Contents.OfType<AgentCore.Conversation.ToolCall>().ToList();
                        if (toolCalls.Any())
                        {
                            assistantMsg.ToolCalls = toolCalls.Select(tc => new LlmTornado.ChatFunctions.ToolCall
                            {
                                Id = tc.Id,
                                Type = "function",
                                FunctionCall = new LlmTornado.ChatFunctions.FunctionCall
                                {
                                    Name = tc.Name,
                                    Arguments = tc.Arguments.ToString()
                                }
                            }).ToList();
                        }
                        chat.AppendMessage(assistantMsg);
                    }
                    break;
                case Role.Tool:
                    {
                        var toolResult = msg.Contents.OfType<AgentCore.Conversation.ToolResult>().FirstOrDefault();
                        var contentStr = toolResult?.Result?.ForLlm() ?? string.Join("\n", msg.Contents.Select(c => c.ForLlm()));
                        var toolMsg = new ChatMessage(ChatMessageRoles.Tool, contentStr ?? string.Empty)
                        {
                            ToolCallId = toolResult?.CallId
                        };
                        chat.AppendMessage(toolMsg);
                    }
                    break;
            }
        }

        var channelOptions = new System.Threading.Channels.BoundedChannelOptions(1024)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
        };
        var channel = System.Threading.Channels.Channel.CreateBounded<IContentDelta>(channelOptions);

        _ = Task.Run(async () =>
        {
            try
            {
                await chat.StreamResponseRich(new ChatStreamEventHandler
                {
                    ReasoningTokenHandler = async (reasoning) =>
                    {
                        if (reasoning.Content is not null)
                        {
                            await channel.Writer.WriteAsync(new ReasoningDelta(reasoning.Content), ct);
                        }
                    },
                    MessagePartHandler = async (part) =>
                    {
                        if (part.Reasoning is not null && part.Reasoning.Content is not null)
                        {
                            await channel.Writer.WriteAsync(new ReasoningDelta(part.Reasoning.Content), ct);
                        }
                        if (part.Text is not null)
                        {
                            await channel.Writer.WriteAsync(new TextDelta(part.Text), ct);
                        }
                    },
                    FunctionCallHandler = async (calls) =>
                    {
                         // Emit tool calls
                         int idx = 0;
                         foreach(var call in calls)
                         {
                              var argsStr = call.Arguments;
                              await channel.Writer.WriteAsync(new ToolCallDelta(idx++, call.ToolCall?.Id ?? Guid.NewGuid().ToString(), call.Name, argsStr), ct);
                         }
                    },
                    OnUsageReceived = async (usage) => 
                    {
                        if (usage.TotalTokens > 0)
                        {
                            await channel.Writer.WriteAsync(new MetaDelta(null, InputTokens: usage.PromptTokens, OutputTokens: usage.CompletionTokens, Model: _model.Name), ct);
                        }
                    }
                }, ct);
            }
            catch (Exception ex)
            {
                var translated = ex;
                if (IsContextLimitError(ex))
                {
                    translated = new ContextLengthExceededException(ex.Message, ex);
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
        var current = ex;
        while (current is not null)
        {
            var msg = current.Message;
            if (msg is not null && (
                msg.Contains("context_length_exceeded", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("maximum context length", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("too many tokens", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("token limit exceeded", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("context window limit", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            current = current.InnerException;
        }
        return false;
    }

    private static bool IsTransientError(Exception ex)
    {
        var current = ex;
        while (current is not null)
        {
            if (current is TimeoutException || 
                current is System.IO.IOException || 
                current is System.Net.Sockets.SocketException)
            {
                return true;
            }

            if (current is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
            {
                var status = httpEx.StatusCode.Value;
                if (status == System.Net.HttpStatusCode.TooManyRequests || 
                    status >= System.Net.HttpStatusCode.InternalServerError)
                {
                    return true;
                }
            }

            var msg = current.Message;
            if (msg is not null && (
                msg.Contains("429", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("503", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("504", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("502", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("500", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("overloaded", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            current = current.InnerException;
        }
        return false;
    }
}

