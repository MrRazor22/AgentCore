using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AgentCore.LLM.Chat
{
    public sealed record Text([property: JsonPropertyName("Value")] string Value) : IContent, ITruncatable
    {
        public static implicit operator Text(string text) => new(text);
        public override string ToString() => Value;

        public IContent Truncate(int maxChars) =>
            Value.Length <= maxChars
                ? this
                : new Text(Value[..maxChars] + $"\n... [Content truncated from {Value.Length} to {maxChars} characters]");

        internal sealed class Accumulator : IContentAccumulator
        {
            private readonly StringBuilder _sb = new();
            public void Append(string chunk) => _sb.Append(chunk);
            public IContent Complete() => new Text(_sb.ToString());
        }
    }

}
