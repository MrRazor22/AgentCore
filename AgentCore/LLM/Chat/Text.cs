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
            int noticeTokens = (int)Math.Ceiling(notice.Length / (double)CharsPerToken);
            int contentBudget = Math.Max(0, maxTokens - noticeTokens);
            int maxChars = contentBudget * CharsPerToken;

            return new Text(Value[..maxChars] + notice);
        }

        public sealed class Stream : IStreamingContent
        {
            private readonly StringBuilder _sb = new();
            public void Recieve(IBlockDeltaEvent delta)
            {
                if (delta is not TextDelta text)
                    throw new InvalidOperationException($"Protocol violation: Stream expected {nameof(TextDelta)} but received {delta.GetType().Name}.");
                _sb.Append(text.Text);
            }
            public IContent ToContent() => new Text(_sb.ToString());
        }
    }

}
