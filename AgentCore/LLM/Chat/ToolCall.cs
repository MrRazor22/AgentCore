using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AgentCore.LLM.Chat
{
    public record ToolCall(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("arguments")] JsonObject Arguments
    ) : IContent
    {
        public virtual int EstimateTokens() => (int)Math.Ceiling((Name.Length + (Arguments?.ToJsonString().Length ?? 0)) / 4.0);

        public virtual IContent Truncate(int maxTokens, string? notice = null) => this;

        public override string ToString()
        {
            if (Arguments.Count == 0)
                return Name;

            var args = string.Join(", ", Arguments.Select(p => $"{p.Key}: {p.Value}"));
            return $"{Name}({args})";
        }
    }
}
