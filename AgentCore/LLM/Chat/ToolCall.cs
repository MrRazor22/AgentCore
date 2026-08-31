using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AgentCore.LLM.Chat
{
    public sealed record ToolCall(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] JsonObject Arguments
) : IContent
    {
        public override string ToString()
        {
            if (Arguments.Count == 0)
                return Name;

            var args = string.Join(", ", Arguments.Select(p => $"{p.Key}: {p.Value}"));
            return $"{Name}({args})";
        }

        internal sealed class Accumulator(string id, string name) : IContentAccumulator
        {
            private readonly StringBuilder _args = new();
            public void Append(string chunk) => _args.Append(chunk);

            public IContent Complete()
            {
                var raw = _args.ToString();
                JsonObject? args = null;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    try
                    {
                        args = JsonNode.Parse(raw)?.AsObject();
                    }
                    catch (Exception ex)
                    {
                        throw new FormatException($"Malformed JSON arguments for tool '{name}' (id: '{id}'): {raw}", ex);
                    }
                }

                return new ToolCall(id, name, args ?? new JsonObject());
            }
        }
    }
}
