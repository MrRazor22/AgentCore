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
    public static MEAILLMClient Create(string baseUrl, string model, string? apiKey = null)
    {
        var credentials = new ApiKeyCredential(apiKey ?? "not-needed");
        var options = new OpenAIClientOptions { Endpoint = new Uri(baseUrl) };
        var openAiClient = new OpenAIClient(credentials, options);
        
        var chatClient = openAiClient.GetChatClient(model);
        var meaiClient = chatClient.AsIChatClient();
        return new MEAILLMClient(meaiClient);
    }

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

                    case Microsoft.Extensions.AI.TextReasoningContent tr:
                        if (!string.IsNullOrEmpty(tr.Text))
                            yield return new ReasoningDelta(tr.Text);
                        break;

                    case FunctionCallContent fc:
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
                        if (u.Details != null)
                        {
                            yield return new MetaDelta(
                                AgentCore.LLM.FinishReason.Stop, 
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
}
