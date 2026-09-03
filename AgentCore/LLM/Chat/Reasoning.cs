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
            int noticeTokens = (int)Math.Ceiling(notice.Length / (double)CharsPerToken);
            int contentBudget = Math.Max(0, maxTokens - noticeTokens);
            int maxChars = contentBudget * CharsPerToken;

            return new Reasoning(Thought[..maxChars] + notice);
        }

        public sealed class Stream : IStreamingContent
        {
            private readonly StringBuilder _sb = new();
            public void Recieve(IBlockDeltaEvent delta)
            {
                if (delta is not ReasoningDelta reasoning)
                    throw new InvalidOperationException($"Protocol violation: Stream expected {nameof(ReasoningDelta)} but received {delta.GetType().Name}.");
                _sb.Append(reasoning.Thought);
            }
            public IContent ToContent() => new Reasoning(_sb.ToString());
        }
    }
}
