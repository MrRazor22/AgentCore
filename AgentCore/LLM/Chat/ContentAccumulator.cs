using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.LLM.Chat;

internal class InternalToolCallBuffer
{
    public Dictionary<int, string> IndexToId { get; } = new();
    public Dictionary<string, StringBuilder> IdToName { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, StringBuilder> IdToArgs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> OrderedIds { get; } = new();
    public HashSet<string> EmittedIds { get; } = new();
    public Type? ActiveDeltaType { get; set; }
}

public static class ContentAccumulationExtensions
{
    private static readonly ConditionalWeakTable<List<IContent>, InternalToolCallBuffer> BufferTable = new();

    public static IContent? AccumulateDelta(
        this List<IContent> contents,
        IContentDelta delta)
    {
        var state = BufferTable.GetOrCreateValue(contents);
        IContent? completedContent = null;

        // Check if delta type transitioned (e.g. ReasoningDelta ended and TextDelta started)
        if (state.ActiveDeltaType != null && state.ActiveDeltaType != delta.GetType())
        {
            if (state.ActiveDeltaType == typeof(ReasoningDelta))
            {
                var lastReasoning = contents.OfType<Reasoning>().LastOrDefault();
                if (lastReasoning != null) completedContent = lastReasoning;
            }
            else if (state.ActiveDeltaType == typeof(TextDelta))
            {
                var lastText = contents.OfType<Text>().LastOrDefault();
                if (lastText != null) completedContent = lastText;
            }
        }

        state.ActiveDeltaType = delta.GetType();

        switch (delta)
        {
            case TextDelta t:
                if (contents.Count > 0 && contents[^1] is Text lastText)
                {
                    contents[^1] = new Text(lastText.Value + t.Value);
                }
                else
                {
                    contents.Add(new Text(t.Value));
                }
                break;

            case ReasoningDelta r:
                if (contents.Count > 0 && contents[^1] is Reasoning lastReasoning)
                {
                    contents[^1] = new Reasoning(lastReasoning.Thought + r.Thought);
                }
                else
                {
                    contents.Add(new Reasoning(r.Thought));
                }
                break;

            case ToolCallDelta tc:
                ProcessToolCallDelta(tc, state);
                SyncToolCallsToContents(contents, state);
                break;
        }

        return completedContent;
    }

    public static IEnumerable<IContent> FlushCompleted(this List<IContent> contents)
    {
        var state = BufferTable.GetOrCreateValue(contents);
        var flushed = new List<IContent>();

        if (state.ActiveDeltaType == typeof(ReasoningDelta))
        {
            var lastReasoning = contents.OfType<Reasoning>().LastOrDefault();
            if (lastReasoning != null) flushed.Add(lastReasoning);
        }
        else if (state.ActiveDeltaType == typeof(TextDelta))
        {
            var lastText = contents.OfType<Text>().LastOrDefault();
            if (lastText != null) flushed.Add(lastText);
        }

        foreach (var tc in contents.OfType<ToolCall>())
        {
            if (state.EmittedIds.Add(tc.Id))
            {
                flushed.Add(tc);
            }
        }

        state.ActiveDeltaType = null;
        return flushed;
    }

    public static List<IContent> Consolidate(this IReadOnlyList<IContent> items)
    {
        var result = new List<IContent>();
        foreach (var item in items)
        {
            switch (item)
            {
                case Text t:
                    var tStr = t.Value.Trim();
                    if (!string.IsNullOrEmpty(tStr)) result.Add(new Text(tStr));
                    break;
                case Reasoning r:
                    var rStr = r.Thought.Trim();
                    if (!string.IsNullOrEmpty(rStr)) result.Add(new Reasoning(rStr));
                    break;
                default:
                    result.Add(item);
                    break;
            }
        }
        return result;
    }

    private static void ProcessToolCallDelta(ToolCallDelta tc, InternalToolCallBuffer state)
    {
        string? key = null;

        if (tc.Index.HasValue)
        {
            if (!state.IndexToId.TryGetValue(tc.Index.Value, out key))
            {
                if (!string.IsNullOrEmpty(tc.Id) && state.IdToName.ContainsKey(tc.Id)) key = tc.Id;
                else key = string.IsNullOrEmpty(tc.Id) ? $"index_{tc.Index.Value}" : tc.Id;
                state.IndexToId[tc.Index.Value] = key;
            }
        }
        else if (!string.IsNullOrEmpty(tc.Id)) key = tc.Id;

        if (key == null)
        {
            if (state.OrderedIds.Count == 1) key = state.OrderedIds[0];
            else if (state.OrderedIds.Count > 1) throw new InvalidOperationException("Ambiguous tool call delta: multiple active tool calls exist.");
            else key = string.IsNullOrEmpty(tc.Id) ? "default" : tc.Id;
        }

        if (state.IdToName.TryAdd(key, new StringBuilder()))
        {
            state.IdToArgs[key] = new StringBuilder();
            state.OrderedIds.Add(key);
        }

        if (tc.Index.HasValue && !string.IsNullOrEmpty(tc.Id) && key.StartsWith("index_") && tc.Id != key)
        {
            var newId = tc.Id;
            state.IndexToId[tc.Index.Value] = newId;
            state.IdToName[newId] = state.IdToName[key];
            state.IdToArgs[newId] = state.IdToArgs[key];
            state.IdToName.Remove(key);
            state.IdToArgs.Remove(key);

            int pos = state.OrderedIds.IndexOf(key);
            if (pos >= 0) state.OrderedIds[pos] = newId;
            key = newId;
        }

        if (!string.IsNullOrEmpty(tc.NameDelta))
        {
            var cur = state.IdToName[key].ToString();
            if (string.IsNullOrEmpty(cur) || (cur != tc.NameDelta && !cur.EndsWith(tc.NameDelta)))
                state.IdToName[key].Append(tc.NameDelta);
        }
        if (!string.IsNullOrEmpty(tc.ArgumentsDelta)) state.IdToArgs[key].Append(tc.ArgumentsDelta);
    }

    private static void SyncToolCallsToContents(List<IContent> contents, InternalToolCallBuffer state)
    {
        contents.RemoveAll(c => c is ToolCall);

        foreach (var id in state.OrderedIds)
        {
            var name = state.IdToName[id].ToString().Trim();
            if (string.IsNullOrEmpty(name)) continue;

            var argsStr = state.IdToArgs[id].ToString().Trim();
            JsonObject? parsedArgs = null;
            if (!string.IsNullOrEmpty(argsStr))
            {
                try { parsedArgs = JsonNode.Parse(argsStr)?.AsObject(); } catch { }
            }
            var finalId = id.StartsWith("index_") || id.Equals("default", StringComparison.OrdinalIgnoreCase) ? "" : id;
            contents.Add(new ToolCall(finalId, name, parsedArgs ?? []));
        }
    }
}

public static class ContentAccumulator
{
    public static async Task<(IReadOnlyList<IContent> Contents, TokenUsage? TokenUsage, FinishReason? FinishReason)> AccumulateAsync(
        this IAsyncEnumerable<ILLMOutput> stream,
        CancellationToken ct = default)
    {
        var contents = new List<IContent>();
        TokenUsage? tokenUsage = null;
        FinishReason? finishReason = null;
        Exception? caughtException = null;

        try
        {
            await foreach (var item in stream.WithCancellation(ct).ConfigureAwait(false))
            {
                switch (item)
                {
                    case IContentDelta delta:
                        contents.AccumulateDelta(delta);
                        break;
                    case TokenUsage tu:
                        tokenUsage = tu;
                        break;
                    case FinishReason fr:
                        finishReason = fr;
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException || ex is System.IO.IOException || ex is System.Net.Http.HttpRequestException)
        {
            caughtException = ex;
        }

        var consolidated = contents.Consolidate();

        if (consolidated.Count == 0)
        {
            if (caughtException != null)
            {
                throw caughtException;
            }
            throw new InvalidOperationException("LLM returned an empty assistant response.");
        }

        return (consolidated, tokenUsage, finishReason);
    }
}
