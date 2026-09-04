using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AgentCore.LLM.Chat
{
    public record Reasoning([property: JsonPropertyName("Thought")] string Thought) : IContent
    {
        private const int CharsPerToken = 4;

        public override string ToString() => Thought;

        public virtual int EstimateTokens() => (int)Math.Ceiling(Thought.Length / (double)CharsPerToken);

        public virtual IContent Truncate(int maxTokens, string? notice = null)
        {
            if (EstimateTokens() <= maxTokens)
                return this;

            notice ??= "\n... [truncated]";
            int maxChars = Math.Max(0, maxTokens * CharsPerToken - notice.Length);

            if (maxChars <= 0)
            {
                int cappedNoticeLen = Math.Min(notice.Length, maxTokens * CharsPerToken);
                return new Reasoning(notice[..cappedNoticeLen]);
            }

            if (maxChars >= Thought.Length)
                return this;

            int headChars = maxChars / 2;
            int tailChars = maxChars - headChars;

            return new Reasoning(Thought[..headChars] + notice + Thought[^tailChars..]);
        }
    }
}
