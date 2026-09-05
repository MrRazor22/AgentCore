using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AgentCore.LLM.Chat
{
    public class ToolCall : IContent
    {
        [JsonPropertyName("id")]
        public string Id { get; }

        [JsonPropertyName("name")]
        public string Name { get; }

        [JsonPropertyName("arguments")]
        public virtual JsonObject Arguments { get; }

        public ToolCall(string id, string name, JsonObject arguments)
        {
            Id = id;
            Name = name;
            Arguments = arguments ?? new JsonObject();
        }

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
