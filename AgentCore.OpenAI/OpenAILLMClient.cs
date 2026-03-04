using AgentCore.Chat;
using AgentCore.LLM;
using AgentCore.Tooling;

using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace AgentCore.Providers.OpenAI;

internal sealed class OpenAILLMClient : ILLMProvider
{
    private readonly OpenAIClient _client;
    private readonly string _defaultModel;

    public OpenAILLMClient(LLMOptions options)
    {
        var opts = options;
        _client = new OpenAIClient(
            new ApiKeyCredential(opts.ApiKey!),
            new OpenAIClientOptions { Endpoint = new Uri(opts.BaseUrl!) }
        );
        _defaultModel = opts.Model ?? throw new InvalidOperationException("Model must be specified in LLMOptions.");
    }

    public async IAsyncEnumerable<IContentDelta> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        IReadOnlyList<Tool>? tools = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = options.Model ?? _defaultModel;
        var chat = _client.GetChatClient(model);
        var chatOptions = BuildChatOptions(options, tools);

        string? pendingToolId = null;
        string? pendingToolName = null;

        await foreach (var update in chat.CompleteChatStreamingAsync(
            messages.ToChatMessages(), chatOptions, ct).WithCancellation(ct))
        {
            if (update.ContentUpdate is { } content)
            {
                foreach (var c in content)
                    if (c.Text is { } t)
                        yield return new TextDelta(t);
            }

            if (update.ToolCallUpdates is { } tcuList)
            {
                foreach (var tcu in tcuList)
                {
                    pendingToolId ??= tcu.ToolCallId;
                    pendingToolName ??= tcu.FunctionName;

                    if (tcu.FunctionArgumentsUpdate is { } argToken)
                    {
                        yield return new ToolCallDelta(
                            Index: tcu.Index,
                            Id: pendingToolId,
                            Name: pendingToolName,
                            ArgumentsDelta: argToken.ToString()
                        );
                    }
                }
            }

            if (update.Usage is { } usage)
                yield return new MetaDelta(FinishReason.Stop, new global::AgentCore.Tokens.TokenUsage(usage.InputTokenCount, usage.OutputTokenCount));

            if (update.FinishReason is { } finish)
                yield return new MetaDelta(finish.ToChatFinishReason(), null);
        }
    }

    private static ChatCompletionOptions BuildChatOptions(LLMOptions options, IReadOnlyList<Tool>? tools)
    {
        var o = new ChatCompletionOptions();
        o.AllowParallelToolCalls = false;

        // Sampling
        o.ApplySamplingOptions(options);

        // Structured output
        if (options.ResponseSchema != null)
        {
            o.ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "structured_response",
                BinaryData.FromString(options.ResponseSchema.ToJsonString()),
                jsonSchemaIsStrict: true
            );
        }

        // Tools
        o.ToolChoice = options.ToolCallMode.ToChatToolChoice();
        if (tools != null)
            foreach (var t in tools.ToChatTools())
                o.Tools.Add(t);

        return o;
    }
}
