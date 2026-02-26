using AgentCore.Chat;
using AgentCore.LLM;
using AgentCore.Tooling;
using Google.GenAI;
using Google.GenAI.Types;
using System.Text.Json.Nodes;

namespace AgentCore.Providers.Gemini;

internal sealed class GeminiLLMClient : ILLMProvider
{
    private readonly Client _client;
    private readonly string _defaultModel;

    public GeminiLLMClient(LLMOptions options, string? project = null, string? location = null)
    {
        if (!string.IsNullOrEmpty(project) && !string.IsNullOrEmpty(location))
            _client = new Client(project: project, location: location, vertexAI: true);
        else
            _client = new Client(apiKey: options.ApiKey);

        _defaultModel = options.Model ?? "gemini-2.0-flash";
    }

    public async IAsyncEnumerable<IContentDelta> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        IReadOnlyList<AgentCore.Tooling.Tool>? tools = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = options.Model ?? _defaultModel;

        await foreach (var chunk in _client.Models.GenerateContentStreamAsync(
            model: model,
            contents: messages.ToGeminiContents(),
            config: options.ToGeminiConfig(tools),
            cancellationToken: ct))
        {
            if (chunk.Candidates is not { Count: > 0 })
                continue;

            var candidate = chunk.Candidates[0];

            if (candidate.Content?.Parts != null)
            {
                foreach (var part in candidate.Content.Parts)
                {
                    if (part.Text is { } text)
                        yield return new TextDelta(text);

                    if (part.FunctionCall is { } funcCall)
                    {
                        var args = funcCall.Args != null
                            ? JsonObject.Parse(System.Text.Json.JsonSerializer.Serialize(funcCall.Args)) as JsonObject
                            : new JsonObject();

                        yield return new ToolCallDelta(
                            Index: 0,
                            Id: Guid.NewGuid().ToString(),
                            Name: funcCall.Name,
                            ArgumentsDelta: args?.ToJsonString() ?? "{}"
                        );
                    }
                }
            }

            if (candidate.FinishReason is { } finish)
                yield return new MetaDelta(finish.Value.ToFinishReason(), null);

            if (chunk.UsageMetadata is { } usage)
                yield return new MetaDelta(AgentCore.LLM.FinishReason.Stop,
                    new AgentCore.Tokens.TokenUsage(usage.PromptTokenCount ?? 0, usage.CandidatesTokenCount ?? 0));
        }
    }
}
