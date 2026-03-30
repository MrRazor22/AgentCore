using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Runtime.CompilerServices;

namespace AgentCore.Providers.MEAI;

public sealed class MEAILLMClient(IChatClient _client) : ILLMProvider
{
    public async IAsyncEnumerable<IContentDelta> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        IReadOnlyList<AgentCore.Tooling.Tool>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var meaiMessages = messages.ToMEAIChatMessages().ToList();
        var meaiOptions = options.ToMEAIChatOptions();
        
        if (tools != null)
        {
            meaiOptions.Tools = tools.ToMEAITools().ToList();
        }

        // MEAI gives us COMPLETE FunctionCallContent objects (not streaming deltas).
        // Each one needs a unique index so LLMExecutor treats them as separate tool calls.
        int toolCallIndex = 0;

        var enumerator = _client.GetStreamingResponseAsync(meaiMessages, meaiOptions, ct).WithCancellation(ct).GetAsyncEnumerator();
        try
        {
            while (true)
            {
                bool hasNext = false;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (Exception ex) when (IsContextLimitException(ex))
                {
                    throw new ContextLengthExceededException("The provider rejected the request due to context length limits.", ex);
                }

                if (!hasNext) break;

                var update = enumerator.Current;
                if (update.FinishReason.HasValue)
                {
                    yield return new MetaDelta(update.FinishReason.ToCoreFinishReason(), null);
                }

                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextContent t:
                            if (!string.IsNullOrEmpty(t.Text))
                                yield return new TextDelta(t.Text);
                            break;

                        case Microsoft.Extensions.AI.TextReasoningContent tr:
                            if (!string.IsNullOrEmpty(tr.Text))
                                yield return new ReasoningDelta(tr.Text);
                            break;

                        case FunctionCallContent fc:
                            var argsDelta = fc.Arguments?.Count > 0 
                                ? System.Text.Json.JsonSerializer.Serialize(fc.Arguments) 
                                : null;

                            yield return new ToolCallDelta(
                                Index: toolCallIndex++,  // unique index per tool call
                                Id: fc.CallId,
                                Name: fc.Name,
                                ArgumentsDelta: argsDelta
                            );
                            break;

                        case UsageContent u:
                            if (u.Details != null)
                            {
                                yield return new MetaDelta(
                                    null, 
                                    new TokenUsage(
                                        (int)(u.Details.InputTokenCount ?? 0), 
                                        (int)(u.Details.OutputTokenCount ?? 0),
                                        (int)(u.Details.ReasoningTokenCount ?? 0)));
                            }
                            break;
                    }
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    private static bool IsContextLimitException(Exception ex)
    {
        var msg = ex.Message.ToLowerInvariant();
        return msg.Contains("maximum context length") || 
               msg.Contains("context_length_exceeded") ||
               (msg.Contains("context") && msg.Contains("exceed")) ||
               (msg.Contains("token") && msg.Contains("exceed")) ||
               msg.Contains("too many tokens");
    }
}

