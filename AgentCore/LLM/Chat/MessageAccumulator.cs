using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.LLM.Chat;

public static class MessageAccumulator
{
    public static async Task<(Message? Message, TokenUsage? TokenUsage, FinishReason? FinishReason)> AccumulateAsync(
        this IAsyncEnumerable<ILLMOutput> stream,
        CancellationToken ct = default)
    {
        var textBuffer = new StringBuilder();
        var reasoningBuffer = new StringBuilder();
        var indexToId = new Dictionary<int, string>();
        var idToName = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
        var idToArgs = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
        var orderedIds = new List<string>();
        TokenUsage? tokenUsage = null;
        FinishReason? finishReason = null;

        await foreach (var item in stream.WithCancellation(ct).ConfigureAwait(false))
        {
            switch (item)
            {
                case TextDelta t: textBuffer.Append(t.Value); break;
                case ReasoningDelta r: reasoningBuffer.Append(r.Thought); break;
                case ToolCallDelta tc:
                    string? key = null;

                    // 1. Resolve key by Index
                    if (tc.Index.HasValue)
                    {
                        if (!indexToId.TryGetValue(tc.Index.Value, out key))
                        {
                            if (!string.IsNullOrEmpty(tc.Id) && idToName.ContainsKey(tc.Id)) key = tc.Id;
                            else key = string.IsNullOrEmpty(tc.Id) ? $"index_{tc.Index.Value}" : tc.Id;
                            indexToId[tc.Index.Value] = key;
                        }
                    }
                    else if (!string.IsNullOrEmpty(tc.Id)) key = tc.Id;

                    // 2. Fallback if both Index and ID are missing
                    if (key == null)
                    {
                        if (orderedIds.Count == 1) key = orderedIds[0];
                        else if (orderedIds.Count > 1) throw new InvalidOperationException("Ambiguous tool call delta: multiple active tool calls exist.");
                        else key = string.IsNullOrEmpty(tc.Id) ? "default" : tc.Id;
                    }

                    // 3. Initialize buffers
                    if (idToName.TryAdd(key, new StringBuilder()))
                    {
                        idToArgs[key] = new StringBuilder();
                        orderedIds.Add(key);
                    }

                    // 4. Migrate key if temporary index converges to real ID
                    if (tc.Index.HasValue && !string.IsNullOrEmpty(tc.Id) && key.StartsWith("index_") && tc.Id != key)
                    {
                        var newId = tc.Id;
                        indexToId[tc.Index.Value] = newId;
                        idToName[newId] = idToName[key];
                        idToArgs[newId] = idToArgs[key];
                        idToName.Remove(key);
                        idToArgs.Remove(key);

                        int pos = orderedIds.IndexOf(key);
                        if (pos >= 0) orderedIds[pos] = newId;
                        key = newId;
                    }

                    // 5. Append Name and Arguments
                    if (!string.IsNullOrEmpty(tc.NameDelta))
                    {
                        var cur = idToName[key].ToString();
                        if (string.IsNullOrEmpty(cur) || (cur != tc.NameDelta && !cur.EndsWith(tc.NameDelta)))
                            idToName[key].Append(tc.NameDelta);
                    }
                    if (!string.IsNullOrEmpty(tc.ArgumentsDelta)) idToArgs[key].Append(tc.ArgumentsDelta);
                    break;
                case TokenUsage tu: tokenUsage = tu; break;
                case FinishReason fr: finishReason = fr; break;
            }
        }

        var finalToolCalls = orderedIds.Select(id => {
            var argsStr = idToArgs[id].ToString().Trim();
            JsonObject? parsedArgs = null;
            if (!string.IsNullOrEmpty(argsStr))
            {
                try { parsedArgs = JsonNode.Parse(argsStr)?.AsObject(); } catch { }
            }
            var finalId = id.StartsWith("index_") || id.Equals("default", StringComparison.OrdinalIgnoreCase) ? "" : id;
            return new ToolCall(finalId, idToName[id].ToString(), parsedArgs ?? []);
        }).ToList();

        var contents = new List<IContent>();
        var reasoningStr = reasoningBuffer.ToString().Trim();
        if (!string.IsNullOrEmpty(reasoningStr)) contents.Add(new Reasoning(reasoningStr));

        var textStr = textBuffer.ToString().Trim();
        if (!string.IsNullOrEmpty(textStr)) contents.Add(new Text(textStr));

        if (finalToolCalls.Count > 0) contents.AddRange(finalToolCalls);

        return (contents.Count == 0 ? null : new Message(Role.Assistant, contents), tokenUsage, finishReason);
    }
}
