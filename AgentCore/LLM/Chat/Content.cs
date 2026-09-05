using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AgentCore.LLM.Chat
{
    public class Text : IContent
    {
        private const int CharsPerToken = 4;

        [JsonPropertyName("Value")]
        public virtual string Value { get; }

        public Text(string value)
        {
            Value = value ?? "";
        }

        public static implicit operator Text(string text) => new(text);
        public override string ToString() => Value;

        public virtual int EstimateTokens() => (int)Math.Ceiling(Value.Length / (double)CharsPerToken);

        public virtual IContent Truncate(int maxTokens, string? notice = null)
        {
            if (EstimateTokens() <= maxTokens)
                return this;

            notice ??= "\n... [truncated]";
            int maxChars = Math.Max(0, maxTokens * CharsPerToken - notice.Length);

            if (maxChars <= 0)
            {
                int cappedNoticeLen = Math.Min(notice.Length, maxTokens * CharsPerToken);
                return new Text(notice[..cappedNoticeLen]);
            }

            if (maxChars >= Value.Length)
                return this;

            int headChars = maxChars / 2;
            int tailChars = maxChars - headChars;

            return new Text(Value[..headChars] + notice + Value[^tailChars..]);
        }
    }

    public class Reasoning(string thought) : Text(thought)
    {
        [JsonPropertyName("Thought")]
        public virtual string Thought => Value;

        public override IContent Truncate(int maxTokens, string? notice = null)
        {
            var truncated = (Text)base.Truncate(maxTokens, notice);
            return ReferenceEquals(truncated, this) ? this : new Reasoning(truncated.Value);
        }
    }

    public class ToolCall : IContent
    {
        [JsonPropertyName("id")]
        public string Id { get; }

        [JsonPropertyName("name")]
        public string Name { get; }

        [JsonPropertyName("arguments")]
        public virtual JsonObject Arguments { get; }

        public ToolCall(string id, string name, JsonObject arguments)
        {
            Id = id;
            Name = name;
            Arguments = arguments ?? new JsonObject();
        }

        public virtual int EstimateTokens() => (int)Math.Ceiling((Name.Length + (Arguments?.ToJsonString().Length ?? 0)) / 4.0);

        public virtual IContent Truncate(int maxTokens, string? notice = null) => this;

        public override string ToString()
        {
            if (Arguments.Count == 0)
                return Name;

            var args = string.Join(", ", Arguments.Select(p => $"{p.Key}: {p.Value}"));
            return $"{Name}({args})";
        }
    }

    public record ToolResult(
        [property: JsonPropertyName("call_id")] string CallId,
        [property: JsonPropertyName("contents")] IReadOnlyList<IContent> Contents
    ) : IContent, IStreamingContent
    {
        public IContent ToContent() => this;

        public override string ToString() => string.Join("\n", Contents.Select(c => c.ToString()));

        public virtual int EstimateTokens() => Contents.Sum(c => c.EstimateTokens());

        public virtual IContent Truncate(int maxTokens, string? notice = null)
        {
            var truncatedList = new List<IContent>();
            int remaining = maxTokens;
            foreach (var c in Contents)
            {
                if (remaining <= 0) break;
                var result = c.Truncate(remaining, notice);
                truncatedList.Add(result);
                remaining -= result.EstimateTokens();
            }
            return new ToolResult(CallId, truncatedList);
        }
    }

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

}
