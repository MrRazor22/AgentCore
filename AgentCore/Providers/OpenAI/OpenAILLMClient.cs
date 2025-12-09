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
            HandlerResolver resolver)
         : base(opts, pipeline, resolver)
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
        private static ChatCompletionOptions ConfigureChatCompletionOptions(LLMRequestBase request)
        {
            var options = new ChatCompletionOptions();
            options.ApplySamplingOptions(request);
            options.AllowParallelToolCalls = false; // intentionally does this to avoud multi tool calls as llm suck at it now

            switch (request)
            {
                case LLMStructuredRequest sreq:
                    options.ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                        "structured_response",
                        BinaryData.FromString(sreq.Schema!.ToString(Newtonsoft.Json.Formatting.None)),
                        jsonSchemaIsStrict: true
                    );

                    options.ToolChoice = sreq.ToolCallMode.ToChatToolChoice();
                    foreach (var t in sreq.ResolvedTools!.ToChatTools()) options.Tools.Add(t);
                    break;

                case LLMTextRequest toolReq:
                    options.ToolChoice = toolReq.ToolCallMode.ToChatToolChoice();
                    foreach (var t in toolReq.ResolvedTools!.ToChatTools()) options.Tools.Add(t);
                    break;

                default:
                    // text-only request: nothing needed
                    break;
            }

            return options;
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

            string? pendingToolName = null;
            string? pendingToolId = null;

            await foreach (var update in stream.WithCancellation(ct))
            {
                // TEXT
                if (update.ContentUpdate is { } content)
                    foreach (var c in content)
                        if (c.Text is { } t)
                            yield return new LLMStreamChunk(StreamKind.Text, t);

                // TOOL CALL DELTA
                if (update.ToolCallUpdates is { } tcuList)
                    foreach (var tcu in tcuList)
                    {
                        pendingToolId ??= tcu.ToolCallId;
                        pendingToolName ??= tcu.FunctionName;

                        if (tcu.FunctionArgumentsUpdate is { } argToken)
                            yield return new LLMStreamChunk(
                                StreamKind.ToolCallDelta,
                                new ToolCallDelta
                                {
                                    Id = pendingToolId,
                                    Name = pendingToolName,
                                    Delta = argToken.ToString()
                                });
                    }

                // USAGE
                if (update.Usage is { } usage)
                    yield return new LLMStreamChunk(
                        StreamKind.Usage,
                        new TokenUsage(usage.InputTokenCount, usage.OutputTokenCount));

                // FINISH
                if (update.FinishReason is { } finish)
                    yield return new LLMStreamChunk(
                        StreamKind.Finish,
                        finish: finish.ToChatFinishReason());
            }
        }
    }
}
