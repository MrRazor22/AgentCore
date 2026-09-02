using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AgentCore.LLM.Chat
{
    public sealed record ToolResult(
    [property: JsonPropertyName("call_id")] string CallId,
    [property: JsonPropertyName("result")] IContent? Result
) : IContent
    {
        public override string ToString() => Result?.ToString() ?? "";

        public int EstimateTokens() => Result?.EstimateTokens() ?? 0;

        public IContent Truncate(int maxTokens)
        {
            if (Result != null)
            {
                var truncated = Result.Truncate(maxTokens);
                return ReferenceEquals(truncated, Result) ? this : new ToolResult(CallId, truncated);
            }
            return this;
        }
    }
}
