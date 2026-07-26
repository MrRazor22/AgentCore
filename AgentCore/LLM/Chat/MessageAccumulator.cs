using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.LLM.Chat;

public static class MessageAccumulator
{
    public static async Task<(Message? Message, Metadata? Metadata)> AccumulateAsync(
        this IAsyncEnumerable<ILLMOutput> stream,
        CancellationToken ct = default)
    {
        var textBuffer = new StringBuilder();
        var reasoningBuffer = new StringBuilder();
        var indexToId = new Dictionary<int, string>();
        var idToName = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
        var idToArgs = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
        var orderedIds = new List<string>();
        var defaultName = new StringBuilder();
        var defaultArgs = new StringBuilder();
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
                    string? key = null;

                    // 1. Resolve key by existing index mapping
                    if (tc.Index.HasValue)
                    {
                        indexToId.TryGetValue(tc.Index.Value, out var mappedId);
                        if (!string.IsNullOrEmpty(mappedId))
                        {
                            key = mappedId;
                        }
                        else
                        {
                            key = $"index_{tc.Index.Value}";
                        }
                    }

                    // 2. Resolve key by ID if not resolved by index
                    if (key == null || key.StartsWith("index_"))
                    {
                        if (!string.IsNullOrEmpty(tc.Id))
                        {
                            // If index key was temporary, we will migrate it later, but check ID mapping first
                            if (idToName.ContainsKey(tc.Id))
                            {
                                key = tc.Id;
                            }
                        }
                    }

                    if (key == null)
                    {
                        key = string.IsNullOrEmpty(tc.Id) ? (orderedIds.LastOrDefault() ?? "default") : tc.Id;
                    }

                    // 3. Initialize buffers
                    if (!idToName.ContainsKey(key))
                    {
                        idToName[key] = new StringBuilder();
                        idToArgs[key] = new StringBuilder();
                        orderedIds.Add(key);
                    }

                    // 4. Append
                    if (!string.IsNullOrEmpty(tc.NameDelta))
                    {
                        var currentName = idToName[key].ToString();
                        if (string.IsNullOrEmpty(currentName))
                        {
                            idToName[key].Append(tc.NameDelta);
                        }
                        else if (currentName != tc.NameDelta && !currentName.EndsWith(tc.NameDelta))
                        {
                            idToName[key].Append(tc.NameDelta);
                        }
                    }
                    if (!string.IsNullOrEmpty(tc.ArgumentsDelta)) idToArgs[key].Append(tc.ArgumentsDelta);

                    // 5. Migrate key if temporary index converges to real ID
                    if (key.StartsWith("index_") && tc.Index.HasValue && !string.IsNullOrEmpty(tc.Id))
                    {
                        var newId = tc.Id;
                        indexToId[tc.Index.Value] = newId;

                        if (newId != key)
                        {
                            idToName[newId] = idToName[key];
                            idToArgs[newId] = idToArgs[key];
                            idToName.Remove(key);
                            idToArgs.Remove(key);

                            int pos = orderedIds.IndexOf(key);
                            if (pos >= 0)
                            {
                                orderedIds[pos] = newId;
                            }
                        }
                    }
                    break;
                case Metadata m:
                    metadata = m;
                    break;
            }
        }

        var finalToolCalls = new List<ToolCall>();
        foreach (var entryId in orderedIds)
        {
            if (!idToName.TryGetValue(entryId, out var nameBuf)) continue;
            var argsBuf = idToArgs.GetValueOrDefault(entryId);

            JsonObject? parsedArgs = null;
            var argsStr = argsBuf?.ToString().Trim() ?? "";
            if (!string.IsNullOrEmpty(argsStr))
            {
                try
                {
                    parsedArgs = JsonNode.Parse(argsStr)?.AsObject();
                }
                catch { }
            }
            // Map index_ prefix to empty ID for ToolCall representation if real ID was never learned
            var finalId = entryId.StartsWith("index_") ? "" : entryId;
            finalToolCalls.Add(new ToolCall(finalId, nameBuf.ToString(), parsedArgs ?? new JsonObject()));
        }

        if (defaultName.Length > 0 || defaultArgs.Length > 0)
        {
            JsonObject? parsedArgs = null;
            var argsStr = defaultArgs.ToString().Trim();
            if (!string.IsNullOrEmpty(argsStr))
            {
                try { parsedArgs = JsonNode.Parse(argsStr)?.AsObject(); } catch { }
            }
            finalToolCalls.Add(new ToolCall("", defaultName.ToString(), parsedArgs ?? new JsonObject()));
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
