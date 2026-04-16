using AgentCore.Memory;
using System.Text;
using System.Text.Json;

namespace AgentCore.Providers.Embeddings;

/// <summary>
/// OpenAI embeddings provider for text-to-vector conversion.
/// Supports OpenAI's text-embedding-3-small and text-embedding-3-large models.
/// </summary>
public sealed class OpenAIEmbeddings : IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _dimensions;

    public OpenAIEmbeddings(
        string apiKey,
        string model = "text-embedding-3-small",
        int dimensions = 1536,
        HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _model = model;
        _dimensions = dimensions;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var requestBody = new
        {
            model = _model,
            input = text,
            dimensions = _dimensions
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

        var response = await _httpClient.PostAsync("https://api.openai.com/v1/embeddings", content, ct);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<OpenAIEmbeddingsResponse>(responseBody);

        if (result?.Data == null || result.Data.Count == 0)
            throw new InvalidOperationException("No embedding returned from OpenAI API");

        return result.Data[0].Embedding;
    }

    private record OpenAIEmbeddingsResponse(
        List<EmbeddingData> Data);

    private record EmbeddingData(
        float[] Embedding);
}
