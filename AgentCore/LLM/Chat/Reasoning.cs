using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AgentCore.LLM.Chat
{
    public sealed record Reasoning([property: JsonPropertyName("Thought")] string Thought) : IContent
    {
        public override string ToString() => Thought;

        internal sealed class Accumulator : IContentAccumulator
        {
            private readonly StringBuilder _sb = new();
            public void Append(string chunk) => _sb.Append(chunk);
            public IContent Complete() => new Reasoning(_sb.ToString());
        }
    }
}
