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

        public int EstimateTokens() => (int)Math.Ceiling(Thought.Length / 4.0);

        public IContent Truncate(int maxTokens)
        {
            int maxChars = Math.Max(0, maxTokens * 4);
            return Thought.Length <= maxChars
                ? this
                : new Reasoning(Thought[..maxChars] + $"\n... [truncated]");
        }

        public sealed class Stream : IStreamingContent
        {
            private readonly StringBuilder _sb = new();
            public void Append(IBlockDeltaEvent delta)
            {
                if (delta is not ReasoningDelta reasoning)
                    throw new InvalidOperationException($"Protocol violation: Stream expected {nameof(ReasoningDelta)} but received {delta.GetType().Name}.");
                _sb.Append(reasoning.Thought);
            }
            public IContent ToContent() => new Reasoning(_sb.ToString());
        }
    }
}
