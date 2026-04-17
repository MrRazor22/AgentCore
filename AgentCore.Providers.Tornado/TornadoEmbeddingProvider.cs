using AgentCore;
using LlmTornado;
using LlmTornado.Embedding;
using LlmTornado.Embedding.Models;

namespace AgentCore.Providers.Tornado;

public class TornadoEmbeddingProvider : IEmbeddingProvider
{
    private readonly TornadoApi _api;
    private readonly EmbeddingModel _model;

    public TornadoEmbeddingProvider(TornadoApi api, EmbeddingModel model)
    {
        _api = api;
        _model = model;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var response = await _api.Embeddings.CreateEmbedding(new EmbeddingRequest
        {
            Model = _model,
            InputScalar = text
        });

        var first = response?.Data?.FirstOrDefault();
        if (first == null)
        {
            return Array.Empty<float>();
        }

        // LlmTornado embeddings data is typically double, cast to float wrapper
        return first.Embedding.Select(x => (float)x).ToArray();
    }
}
