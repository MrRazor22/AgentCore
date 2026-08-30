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
        public string ForLlm()
            => Result?.ForLlm() ?? "";
    }
}
