using AgentCore.Json;
using AgentCore.LLM.Execution;
using AgentCore.LLM.Protocol;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Collections.Concurrent;

namespace AgentCore.Providers.OpenAI;

internal sealed class OpenAILLMClient : ILLMStreamProvider
{
    private OpenAIClient? _client;
    private readonly ConcurrentDictionary<string, ChatClient> _chatClients = new(StringComparer.OrdinalIgnoreCase);
    private string? _defaultModel;

    public OpenAILLMClient(IOptions<LLMInitOptions> options, ILogger<OpenAILLMClient> logger)
    {
        var opts = options.Value;
        _client = new OpenAIClient(
            new ApiKeyCredential(opts.ApiKey!),
            new OpenAIClientOptions { Endpoint = new Uri(opts.BaseUrl!) }
        );
        _defaultModel = opts.Model;
        _chatClients[_defaultModel!] = _client.GetChatClient(_defaultModel);
    }

    private ChatClient GetChatClient(string? model = null)
    {
        if (_client == null) throw new InvalidOperationException("Client not initialized.");
        var key = model ?? _defaultModel ?? throw new InvalidOperationException("Model not specified.");
        return _chatClients.GetOrAdd(key, m => _client.GetChatClient(m));
    }

    private static ChatCompletionOptions ConfigureChatCompletionOptions(LLMRequest request)
    {
        var options = new ChatCompletionOptions();
        options.ApplySamplingOptions(request);
        options.AllowParallelToolCalls = false;

        if (request.OutputType != null)
        {
            options.ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "structured_response",
                BinaryData.FromString(request.OutputType.GetSchemaForType().ToString(Newtonsoft.Json.Formatting.None)),
                jsonSchemaIsStrict: true
            );
        }

        options.ToolChoice = request.ToolCallMode.ToChatToolChoice();

        if (request.AvailableTools != null)
            foreach (var t in request.AvailableTools.ToChatTools())
                options.Tools.Add(t);

        return options;
    }

    public async IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var chat = GetChatClient(request.Model);
        var options = ConfigureChatCompletionOptions(request);

        var stream = chat.CompleteChatStreamingAsync(request.Prompt.ToChatMessages(), options, ct);

        string? pendingToolName = null;
        string? pendingToolId = null;

        await foreach (var update in stream.WithCancellation(ct))
        {
            if (update.ContentUpdate is { } content)
            {
                foreach (var c in content)
                {
                    if (c.Text is { } t)
                    {
                        yield return new LLMStreamChunk(
                            request.OutputType != null ? StreamKind.Structured : StreamKind.Text,
                            t);
                    }
                }
            }

            if (update.ToolCallUpdates is { } tcuList)
            {
                foreach (var tcu in tcuList)
                {
                    pendingToolId ??= tcu.ToolCallId;
                    pendingToolName ??= tcu.FunctionName;

                    if (tcu.FunctionArgumentsUpdate is { } argToken)
                    {
                        yield return new LLMStreamChunk(StreamKind.ToolCallDelta, new ToolCallDelta
                        {
                            Id = pendingToolId,
                            Name = pendingToolName,
                            Delta = argToken.ToString()
                        });
                    }
                }
            }

            if (update.Usage is { } usage)
                yield return new LLMStreamChunk(StreamKind.Usage, new TokenUsage(usage.InputTokenCount, usage.OutputTokenCount));

            if (update.FinishReason is { } finish)
                yield return new LLMStreamChunk(StreamKind.Finish, finish.ToChatFinishReason());
        }
    }
}
