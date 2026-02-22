using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.LLM.Protocol;
using AgentCore.Providers;
using AgentCore.Tokens;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;

namespace AgentCore.Providers.Gemini;

internal sealed class GeminiLLMClient : ILLMStreamProvider
{
    private readonly Client _client;
    private readonly string _defaultModel;
    private readonly ILogger<GeminiLLMClient> _logger;

    public GeminiLLMClient(IOptions<GeminiInitOptions> options, ILogger<GeminiLLMClient> logger)
    {
        var opts = options.Value;
        _logger = logger;

        if (!string.IsNullOrEmpty(opts.Project) && !string.IsNullOrEmpty(opts.Location))
        {
            _client = new Client(project: opts.Project, location: opts.Location, vertexAI: true);
        }
        else
        {
            _client = new Client(apiKey: opts.ApiKey);
        }

        _defaultModel = opts.Model ?? "gemini-2.0-flash";
    }

    public async IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = request.Model ?? _defaultModel;
        var contents = request.Prompt.ToGeminiContents();
        var config = request.ToGeminiConfig();

        await foreach (var chunk in _client.Models.GenerateContentStreamAsync(
            model: model,
            contents: contents,
            config: config,
            cancellationToken: ct
        ))
        {
            if (chunk.Candidates == null || chunk.Candidates.Count == 0)
                continue;

            var candidate = chunk.Candidates[0];

            if (candidate.Content?.Parts != null)
            {
                foreach (var part in candidate.Content.Parts)
                {
                    if (part.Text is { } text)
                    {
                        yield return new LLMStreamChunk(
                            request.OutputType != null ? StreamKind.Structured : StreamKind.Text,
                            text);
                    }

                    if (part.FunctionCall is { } funcCall)
                    {
                        var args = funcCall.Args != null
                            ? JsonObject.Parse(System.Text.Json.JsonSerializer.Serialize(funcCall.Args)) as JsonObject
                            : new JsonObject();

                        yield return new LLMStreamChunk(StreamKind.ToolCallDelta, new ToolCallDelta
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = funcCall.Name,
                            Delta = args?.ToJsonString() ?? "{}"
                        });
                    }
                }
            }

            if (candidate.FinishReason is { } finish)
            {
                yield return new LLMStreamChunk(StreamKind.Finish, finish.Value.ToFinishReason());
            }

            if (chunk.UsageMetadata is { } usage)
            {
                yield return new LLMStreamChunk(StreamKind.Usage, new TokenUsage(
                    usage.PromptTokenCount ?? 0,
                    usage.CandidatesTokenCount ?? 0
                ));
            }
        }
    }
}
