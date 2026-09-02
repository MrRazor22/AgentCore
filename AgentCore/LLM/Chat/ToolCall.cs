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
        public int EstimateTokens() => (int)Math.Ceiling((Name.Length + (Arguments?.ToJsonString().Length ?? 0)) / 4.0);

        public IContent Truncate(int maxTokens) => this;

        public override string ToString()
        {
            if (Arguments.Count == 0)
                return Name;

            var args = string.Join(", ", Arguments.Select(p => $"{p.Key}: {p.Value}"));
            return $"{Name}({args})";
        }

        public sealed class Stream(string id, string name) : IStreamingContent
        {
            private readonly StringBuilder _args = new();
            public void Recieve(IBlockDeltaEvent delta)
            {
                if (delta is not ToolCallDelta toolCall)
                    throw new InvalidOperationException($"Protocol violation: Stream expected {nameof(ToolCallDelta)} but received {delta.GetType().Name}.");
                _args.Append(toolCall.Arguments);
            }

            public IContent ToContent()
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
