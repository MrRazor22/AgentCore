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
        public string ForLlm() => Value;

        internal sealed class Accumulator : IContentAccumulator
        {
            private readonly StringBuilder _sb = new();
            public void Append(string chunk) => _sb.Append(chunk);
            public IContent Complete() => new Text(_sb.ToString());
        }
    }

}
