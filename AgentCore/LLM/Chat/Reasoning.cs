using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AgentCore.LLM.Chat
{
    public sealed record Reasoning([property: JsonPropertyName("Thought")] string Thought) : IContent, IEstimatable
    {
        public override string ToString() => Thought;

        public int EstimateTokens() => (int)Math.Ceiling(Thought.Length / 4.0);

        public sealed class Stream : IStreamingContent<string>
        {
            private readonly StringBuilder _sb = new();
            public void Append(string chunk) => _sb.Append(chunk);
            public IContent ToContent() => new Reasoning(_sb.ToString());
        }
    }
}
