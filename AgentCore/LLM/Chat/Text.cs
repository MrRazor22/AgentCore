using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AgentCore.LLM.Chat
{
    public sealed record Text([property: JsonPropertyName("Value")] string Value) : IContent, ITruncatable, IEstimatable
    {
        public static implicit operator Text(string text) => new(text);
        public override string ToString() => Value;

        public int EstimateTokens() => (int)Math.Ceiling(Value.Length / 4.0);

        public IContent Truncate(int maxTokens)
        {
            int maxChars = Math.Max(0, maxTokens * 4);
            return Value.Length <= maxChars
                ? this
                : new Text(Value[..maxChars] + $"\n... [Content truncated from {Value.Length} to {maxChars} characters]");
        }

        public sealed class Stream : IStreamingContent<string>
        {
            private readonly StringBuilder _sb = new();
            public void Append(string chunk) => _sb.Append(chunk);
            public IContent ToContent() => new Text(_sb.ToString());
        }
    }

}
