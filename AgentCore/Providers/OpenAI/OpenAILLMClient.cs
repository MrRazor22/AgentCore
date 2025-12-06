using AgentCore.LLM.Client;
using AgentCore.LLM.Handlers;
using AgentCore.LLM.Pipeline;
using AgentCore.Tokens;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.Providers.OpenAI
{
    internal sealed class OpenAILLMClient : LLMClientBase
    {
        private OpenAIClient? _client;
        private readonly ConcurrentDictionary<string, ChatClient> _chatClients =
            new ConcurrentDictionary<string, ChatClient>(StringComparer.OrdinalIgnoreCase);

        private string? _defaultModel;
        public OpenAILLMClient(
            LLMInitOptions opts,
            ILLMPipeline pipeline,
            TextHandlerFactory textFactory,
            StructuredHandlerFactory structFactory,
            ILogger<ILLMClient> logger)
         : base(opts, pipeline, textFactory, structFactory, logger)
        {
            _client = new OpenAIClient(
                credential: new ApiKeyCredential(_initOptions.ApiKey!),
                options: new OpenAIClientOptions { Endpoint = new Uri(_initOptions.BaseUrl) }
            );

            _defaultModel = _initOptions.Model;
            _chatClients[_defaultModel!] = _client.GetChatClient(_defaultModel);
        }

        private ChatClient GetChatClient(string? model = null)
        {
            if (_client == null)
                throw new InvalidOperationException("Client not initialized. Call Initialize() first.");

            var key = model ?? _defaultModel ?? throw new InvalidOperationException("Model not specified.");
            return _chatClients.GetOrAdd(key, m => _client.GetChatClient(m));
        }

        protected override async IAsyncEnumerable<LLMStreamChunk> StreamAsync(
            LLMRequestBase request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var chat = GetChatClient(request.Model);
            ChatCompletionOptions options = ConfigureChatCompletionOptions(request);

            var stream = chat.CompleteChatStreamingAsync(
                request.Prompt.ToChatMessages(),
                options,
                ct
            );

            string? pendingName = null;
            string? pendingId = null;

            await foreach (var update in stream.WithCancellation(ct))
            {
                if (update.ContentUpdate != null)
                {
                    foreach (var c in update.ContentUpdate)
                        if (c.Text != null)
                            yield return new LLMStreamChunk(StreamKind.Text, c.Text);
                }

                if (update.ToolCallUpdates != null)
                {
                    foreach (var tcu in update.ToolCallUpdates)
                    {
                        pendingId ??= tcu.ToolCallId;
                        pendingName ??= tcu.FunctionName;
                        var delta = tcu.FunctionArgumentsUpdate?.ToString();

                        if (!string.IsNullOrEmpty(delta))
                        {
                            yield return new LLMStreamChunk(
                                StreamKind.ToolCallDelta,
                                new ToolCallDelta { Id = pendingId, Name = pendingName, Delta = delta }
                            );
                        }
                    }
                }

                if (update.Usage != null)
                {
                    yield return new LLMStreamChunk(
                        StreamKind.Usage,
                        new TokenUsage(update.Usage.InputTokenCount, update.Usage.OutputTokenCount)
                    );
                }

                if (update.FinishReason != null)
                {
                    yield return new LLMStreamChunk(
                        StreamKind.Finish,
                        finish: update.FinishReason.Value.ToString()
                    );
                }
            }
        }

        private static ChatCompletionOptions ConfigureChatCompletionOptions(LLMRequestBase request)
        {
            var options = new ChatCompletionOptions();
            options.ApplySamplingOptions(request);
            switch (request)
            {
                case LLMStructuredRequest sreq:
                    options.ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                        "structured_response",
                        BinaryData.FromString(
                            sreq.Schema!.ToString(Newtonsoft.Json.Formatting.None)
                        ),
                        jsonSchemaIsStrict: true
                    );

                    // ALSO tools if allowed (Structured may allow tools)
                    if (sreq.AllowedTools != null)
                    {
                        options.ToolChoice = sreq.ToolCallMode.ToChatToolChoice();
                        options.AllowParallelToolCalls = false; // intentionally does this to avoud multi tool calls as llm suck at it now

                        foreach (var t in sreq.AllowedTools.ToChatTools())
                            options.Tools.Add(t);
                    }
                    break;

                case LLMRequest toolReq:
                    // tool-only request
                    options.ToolChoice = toolReq.ToolCallMode.ToChatToolChoice();
                    options.AllowParallelToolCalls = false;

                    foreach (var t in toolReq.AllowedTools!.ToChatTools())
                        options.Tools.Add(t);

                    break;

                default:
                    // text-only request: nothing needed
                    break;
            }

            return options;
        }
    }
}
