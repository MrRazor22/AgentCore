using System;
using System.Linq;
using AgentCore.LLM.Chat;

namespace AgentCore.Context.Primitives;

public interface ITokenizer
{
    int CharsPerToken { get; }
    int Estimate(IContent content);
}

public sealed class Tokenizer(
    int charsPerToken = 4,
    double pixelsPerToken = 750.0,
    int defaultImageTokens = 1000,
    int minImageTokens = 85,
    int defaultAudioTokens = 1000,
    int defaultVideoTokens = 2000) : ITokenizer
{
    public int CharsPerToken { get; } = charsPerToken > 0 ? charsPerToken : 4;

    public int Estimate(IContent content) => content switch
    {
        Text t => (int)Math.Ceiling(t.Value.Length / (double)CharsPerToken),
        Reasoning r => (int)Math.Ceiling(r.Thought.Length / (double)CharsPerToken),
        ToolCall tc => (int)Math.Ceiling((tc.Name.Length + (tc.Arguments?.Length ?? 0)) / (double)CharsPerToken),
        Image img => EstimateImage(img),
        Audio a => a.Duration is { TotalSeconds: > 0 } d ? (int)Math.Ceiling(d * 25) : defaultAudioTokens,
        Video v => v.Duration is { TotalSeconds: > 0 } d ? (int)Math.Ceiling(d * 300) : defaultVideoTokens,
        _ => 0
    };

    private int EstimateImage(Image img)
    {
        if (img.Width is > 0 && img.Height is > 0)
        {
            long pixels = (long)img.Width.Value * img.Height.Value;
            return (int)Math.Min(int.MaxValue, Math.Max(minImageTokens, Math.Ceiling(pixels / pixelsPerToken)));
        }
        return defaultImageTokens;
    }
}
