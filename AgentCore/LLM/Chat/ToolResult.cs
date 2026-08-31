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
) : IContent, ITruncatable
    {
        public override string ToString() => Result?.ToString() ?? "";

        public IContent Truncate(int maxChars)
        {
            if (Result is ITruncatable truncatable)
            {
                var truncated = truncatable.Truncate(maxChars);
                return ReferenceEquals(truncated, Result) ? this : new ToolResult(CallId, truncated);
            }
            return this;
        }
    }
}
