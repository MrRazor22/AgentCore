using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AgentCore.LLM.Chat
{
    public record Text([property: JsonPropertyName("Value")] string Value) : IContent
    {
        private const int CharsPerToken = 4;

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
}
