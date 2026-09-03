using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AgentCore.LLM.Chat
{
    public record ToolResult(
        [property: JsonPropertyName("call_id")] string CallId,
        [property: JsonPropertyName("contents")] IReadOnlyList<IContent> Contents
    ) : IContent
    {
        public override string ToString() => string.Join("\n", Contents.Select(c => c.ToString()));

        public virtual int EstimateTokens() => Contents.Sum(c => c.EstimateTokens());

        public virtual IContent Truncate(int maxTokens, string? notice = null)
        {
            var truncatedList = new List<IContent>();
            int remaining = maxTokens;
            foreach (var c in Contents)
            {
                if (remaining <= 0) break;
                var result = c.Truncate(remaining, notice);
                truncatedList.Add(result);
                remaining -= result.EstimateTokens();
            }
            return new ToolResult(CallId, truncatedList);
        }
    }
}
