using AgentCore.Chat;
using AgentCore.LLM;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace AgentCore.Providers.MEAI;

internal sealed class MEAILLMClient(IChatClient _client) : ILLMProvider
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

        await foreach (var update in _client.GetStreamingResponseAsync(meaiMessages, meaiOptions, ct).WithCancellation(ct))
        {
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

                    case FunctionCallContent fc:
                        // MEAI delivers function call updates. 
                        yield return new ToolCallDelta(
                            Index: 0, 
                            Id: fc.CallId,
                            Name: fc.Name,
                            ArgumentsDelta: fc.Arguments?.Count > 0 
                                ? System.Text.Json.JsonSerializer.Serialize(fc.Arguments) 
                                : null
                        );
                        break;

                    case UsageContent u:
                        yield return new MetaDelta(
                            AgentCore.LLM.FinishReason.Stop, 
                            new TokenUsage(
                                (int)u.Details.InputTokenCount, 
                                (int)u.Details.OutputTokenCount,
                                (int)u.Details.ReasoningTokenCount));
                        break;
                }
            }
        }
    }
}
