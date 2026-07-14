using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.LLM.Exceptions;
using LlmTornado.Chat;
using LlmTornado.Code;
using LlmTornado.Common;

namespace AgentCore.Providers.Tornado;

internal static class TornadoAdapterExtensions
{
    public static Exception TranslateException(this Exception ex)
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
                return new ContextLengthExceededException(ex.Message, ex);
            }

            if (current is TimeoutException || 
                current is System.IO.IOException || 
                current is System.Net.Sockets.SocketException)
            {
                return new RetryableException(ex.Message, ex);
            }

            if (current is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
            {
                var status = httpEx.StatusCode.Value;
                if (status == System.Net.HttpStatusCode.TooManyRequests || 
                    status >= System.Net.HttpStatusCode.InternalServerError)
                {
                    return new RetryableException(ex.Message, ex);
                }
            }

            if (msg is not null && (
                msg.Contains("429", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("503", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("504", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("502", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("500", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("overloaded", StringComparison.OrdinalIgnoreCase)))
            {
                return new RetryableException(ex.Message, ex);
            }

            current = current.InnerException;
        }
        return ex;
    }

    public static void ApplyOptions(this ChatRequest request, LLMOptions options, IReadOnlyList<AgentCore.Tools.Tool>? tools)
    {
        if (tools is { Count: > 0 })
        {
            request.Tools = tools.Select(t =>
                new LlmTornado.Common.Tool(new ToolFunction(t.Name, t.Description, t.ParametersSchema.ToString()))
            ).ToList();
        }

        if (options.Temperature.HasValue) request.Temperature = options.Temperature.Value;
        if (options.MaxOutputTokens.HasValue) request.MaxTokens = options.MaxOutputTokens.Value;
        
        if (options.ResponseSchema != null)
        {
            request.ResponseFormat = ChatRequestResponseFormats.StructuredJson("response_schema", options.ResponseSchema.ToJsonElement());
        }

        request.ReasoningFormat = LlmTornado.Code.ChatReasoningFormats.Parsed;
    }

    public static ChatMessage ToTornadoMessage(this Message msg)
    {
        switch (msg.Role)
        {
            case Role.System:
                {
                    var contentStr = string.Join("\n", msg.Contents.Select(c => c.ForLlm()));
                    return new ChatMessage(ChatMessageRoles.System, contentStr);
                }
            case Role.User:
                {
                    var contentStr = string.Join("\n", msg.Contents.Select(c => c.ForLlm()));
                    return new ChatMessage(ChatMessageRoles.User, contentStr);
                }
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
                    return assistantMsg;
                }
            case Role.Tool:
                {
                    var toolResult = msg.Contents.OfType<AgentCore.Conversation.ToolResult>().FirstOrDefault();
                    var contentStr = toolResult?.Result?.ForLlm() ?? string.Join("\n", msg.Contents.Select(c => c.ForLlm()));
                    return new ChatMessage(ChatMessageRoles.Tool, contentStr ?? string.Empty)
                    {
                        ToolCallId = toolResult?.CallId
                    };
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(msg.Role), $"Unsupported role: {msg.Role}");
        }
    }
}
