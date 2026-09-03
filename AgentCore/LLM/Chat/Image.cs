using System;
using System.Text.Json.Serialization;

namespace AgentCore.LLM.Chat;

public record Image(
    ReadOnlyMemory<byte>? Data = null,
    Uri? Uri = null,
    [property: JsonPropertyName("media_type")] string MediaType = "image/png",
    [property: JsonPropertyName("width")] int? Width = null,
    [property: JsonPropertyName("height")] int? Height = null) : IContent
{
    // Framework heuristics for preflight context budgeting (provider token accounting will vary)
    private const int DefaultEstimatedTokens = 1000;
    private const int MinEstimatedTokens = 85;
    private const double PixelsPerToken = 750.0;

    public virtual int EstimateTokens()
    {
        if (Width is > 0 && Height is > 0)
        {
            long pixels = (long)Width.Value * Height.Value;
            double estimated = Math.Ceiling(pixels / PixelsPerToken);
            return (int)Math.Min(int.MaxValue, Math.Max(MinEstimatedTokens, estimated));
        }

        return DefaultEstimatedTokens;
    }

    public virtual IContent Truncate(int maxTokens, string? notice = null)
    {
        if (EstimateTokens() <= maxTokens)
            return this;

        return new Text(notice ?? $"[Image ({MediaType}) omitted: exceeds context budget]");
    }
}
