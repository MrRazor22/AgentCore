using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AgentCore.LLM.Chat
{
    public sealed record Text([property: JsonPropertyName("Value")] string Value) : IContent
    {
        public static implicit operator Text(string text) => new(text);
        public override string ToString() => Value;

        public int EstimateTokens() => (int)Math.Ceiling(Value.Length / 4.0);

        public IContent Truncate(int maxTokens)
        {
            int maxChars = Math.Max(0, maxTokens * 4);
            return Value.Length <= maxChars
                ? this
                : new Text(Value[..maxChars] + $"\n... [truncated]");
        }

        public sealed class Stream : IStreamingContent
        {
            private readonly StringBuilder _sb = new();
            public void Append(IBlockDeltaEvent delta)
            {
                if (delta is not TextDelta text)
                    throw new InvalidOperationException($"Protocol violation: Stream expected {nameof(TextDelta)} but received {delta.GetType().Name}.");
                _sb.Append(text.Text);
            }
            public IContent ToContent() => new Text(_sb.ToString());
        }
    }

}
