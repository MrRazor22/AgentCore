using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;

namespace AgentCore.LLM;

internal static class LLMStreamExtensions
{
    public static async Task<Message?> AccumulateAsync(
        this IAsyncEnumerable<LLMEvent> stream,
        CancellationToken ct = default)
    {
        var textBuffer = new StringBuilder();
        var reasoningBuffer = new StringBuilder();
        var toolCalls = new Dictionary<int, (string id, string name, StringBuilder args)>();

        await foreach (var evt in stream.WithCancellation(ct).ConfigureAwait(false))
        {
            switch (evt)
            {
                case Text t:
                    textBuffer.Append(t.Value);
                    break;
                case Reasoning r:
                    reasoningBuffer.Append(r.Thought);
                    break;
                case ToolCall tc:
                    int index = tc.Index ?? 0;
                    if (!toolCalls.TryGetValue(index, out var entry))
                        entry = ("", "", new StringBuilder());
                    if (!string.IsNullOrEmpty(tc.Id)) entry.id = tc.Id;
                    if (!string.IsNullOrEmpty(tc.Name)) entry.name = tc.Name;
                    if (tc.Arguments != null)
                    {
                        if (tc.Arguments is JsonValue val && val.TryGetValue<string>(out var str))
                        {
                            entry.args.Append(str);
                        }
                        else if (tc.Arguments is JsonObject obj)
                        {
                            entry.args.Append(obj.ToJsonString());
                        }
                        else
                        {
                            entry.args.Append(tc.Arguments.ToString());
                        }
                    }
                    toolCalls[index] = entry;
                    break;
            }
        }

        var finalToolCalls = new List<ToolCall>();
        foreach (var pair in toolCalls.OrderBy(p => p.Key))
        {
            var entry = pair.Value;
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

        return contents.Count == 0 ? null : new Message(Role.Assistant, contents);
    }
}
