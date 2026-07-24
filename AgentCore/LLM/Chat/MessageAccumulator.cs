using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.LLM.Chat;

public static class MessageAccumulator
{
    public static async Task<(Message? Message, Metadata? Metadata)> AccumulateAsync(
        this IAsyncEnumerable<ILLMOutput> stream,
        CancellationToken ct = default)
    {
        var textBuffer = new StringBuilder();
        var reasoningBuffer = new StringBuilder();
        var toolCalls = new Dictionary<string, (string id, string name, StringBuilder args)>();
        Metadata? metadata = null;

        await foreach (var item in stream.WithCancellation(ct).ConfigureAwait(false))
        {
            switch (item)
            {
                case TextDelta t:
                    textBuffer.Append(t.Value);
                    break;
                case ReasoningDelta r:
                    reasoningBuffer.Append(r.Thought);
                    break;
                case ToolCallDelta tc:
                    var key = string.IsNullOrEmpty(tc.Id) ? (toolCalls.Keys.LastOrDefault() ?? "default") : tc.Id;
                    if (!toolCalls.TryGetValue(key, out var entry))
                        entry = (key, "", new StringBuilder());
                    
                    if (!string.IsNullOrEmpty(tc.NameDelta)) entry.name += tc.NameDelta;
                    if (!string.IsNullOrEmpty(tc.ArgumentsDelta)) entry.args.Append(tc.ArgumentsDelta);
                    toolCalls[key] = entry;
                    break;
                case Metadata m:
                    metadata = m;
                    break;
            }
        }

        var finalToolCalls = new List<ToolCall>();
        foreach (var entry in toolCalls.Values)
        {
            JsonObject? parsedArgs = null;
            var argsStr = entry.args.ToString().Trim();
            if (!string.IsNullOrEmpty(argsStr))
            {
                try
                {
                    parsedArgs = JsonNode.Parse(argsStr)?.AsObject();
                }
                catch { }
            }
            finalToolCalls.Add(new ToolCall(entry.id, entry.name, parsedArgs ?? new JsonObject()));
        }

        var contents = new List<IContent>();
        var reasoningStr = reasoningBuffer.ToString().Trim();
        if (!string.IsNullOrEmpty(reasoningStr))
        {
            contents.Add(new Reasoning(reasoningStr));
        }

        var textStr = textBuffer.ToString().Trim();
        if (!string.IsNullOrEmpty(textStr))
        {
            contents.Add(new Text(textStr));
        }

        if (finalToolCalls.Count > 0)
        {
            contents.AddRange(finalToolCalls);
        }

        var message = contents.Count == 0 ? null : new Message(Role.Assistant, contents);
        return (message, metadata);
    }
}
