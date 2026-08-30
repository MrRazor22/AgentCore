using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AgentCore.Layers.LLM;

public class ToolCallDetectionOptions
{
    public bool StopAfterFirstToolCall { get; set; } = false;
}

public class ToolCallDetectionLayer(ToolCallDetectionOptions? options = null) : LLMLayer
{
    private readonly ToolCallDetectionOptions _options = options ?? new();

    private static readonly Regex TagPattern = new(
        @"[\[\(<](?<tag>[^\]\)>]*?tool[^\]\)>]*?)[\]\)>]\s*(?<content>[\s\S]*?)\s*[\[\(<]/\k<tag>[\]\)>]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex MarkdownPattern = new(
        @"```json\s*(?<json>\{[\s\S]*?\})\s*```",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex XmlFuncPattern = new(
        @"(?i)<function\s*(?:=|\bname\s*=)\s*""?(?<name>[a-zA-Z0-9_\-]+)""?\s*>", RegexOptions.Compiled);

    private static readonly Regex XmlParamPattern = new(
        @"<parameter\s*=\s*""?(?<name>[a-zA-Z0-9_\-]+)""?\s*>(?<val>[\s\S]*?)</parameter>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public override IAsyncEnumerable<IMessageEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        CancellationToken ct = default)
    {
        var toolNames = tools?.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        return toolNames.Count == 0
            ? Inner.StreamAsync(messages, responseSchema, tools, ct)
            : ProcessStreamAsync(Inner.StreamAsync(messages, responseSchema, tools, ct), toolNames, ct);
    }

    private async IAsyncEnumerable<IMessageEvent> ProcessStreamAsync(
        IAsyncEnumerable<IMessageEvent> innerStream,
        HashSet<string> toolNames,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var buffers = new Dictionary<int, StringBuilder>();
        int eventIndex = 0;

        await foreach (var evt in innerStream.WithCancellation(ct).ConfigureAwait(false))
        {
            yield return evt;

            switch (evt)
            {
                case TextStart s:
                    buffers[s.Index] = new();
                    eventIndex = Math.Max(eventIndex, s.Index + 1);
                    break;

                case ReasoningStart rs:
                    buffers[rs.Index] = new();
                    eventIndex = Math.Max(eventIndex, rs.Index + 1);
                    break;

                case TextDelta d:
                    if (!buffers.TryGetValue(d.Index, out var tb)) buffers[d.Index] = tb = new();
                    tb.Append(d.Text);
                    break;

                case ReasoningDelta rd:
                    if (!buffers.TryGetValue(rd.Index, out var rb)) buffers[rd.Index] = rb = new();
                    rb.Append(rd.Thought);
                    break;

                case TextEnd te when buffers.Remove(te.Index, out var sb):
                    foreach (var tcEvt in EmitToolCalls(sb.ToString()))
                    {
                        yield return tcEvt;
                        if (_options.StopAfterFirstToolCall) yield break;
                    }
                    break;

                case ReasoningEnd re when buffers.Remove(re.Index, out var sb):
                    foreach (var tcEvt in EmitToolCalls(sb.ToString()))
                    {
                        yield return tcEvt;
                        if (_options.StopAfterFirstToolCall) yield break;
                    }
                    break;

                case MessageEnd:
                    foreach (var (_, sb) in buffers)
                        foreach (var tcEvt in EmitToolCalls(sb.ToString()))
                        {
                            yield return tcEvt;
                            if (_options.StopAfterFirstToolCall) yield break;
                        }
                    buffers.Clear();
                    break;
            }
        }

        IEnumerable<IMessageEvent> EmitToolCalls(string text)
        {
            int lastIndex = 0;
            while (lastIndex < text.Length)
            {
                var match = FindToolCall(text, lastIndex, toolNames);
                if (match == null) break;

                int idx = eventIndex++;
                yield return new ToolCallStart(idx, match.Call.Id, match.Call.Name);
                yield return new ToolCallDelta(idx, match.Call.Arguments?.ToJsonString() ?? "{}");
                yield return new ToolCallEnd(idx);

                lastIndex = match.Index + match.Length;
            }
        }
    }

    private record ToolMatch(ToolCall Call, int Index, int Length);

    private static ToolMatch? FindToolCall(string text, int startIndex, HashSet<string> toolNames)
    {
        // 1. Tag wrapper (<tool_call>...</tool_call>)
        var tag = TagPattern.Match(text, startIndex);
        if (tag.Success && ExtractTag(tag.Groups["content"].Value, toolNames) is { } tc1)
            return new(tc1, tag.Index, tag.Length);

        // 2. Markdown codeblock (```json { ... } ```)
        var md = MarkdownPattern.Match(text, startIndex);
        if (md.Success && ExtractJson(md.Groups["json"].Value, toolNames) is { } tc2)
            return new(tc2, md.Index, md.Length);

        // 3. Raw JSON ({ "name": "...", "arguments": { ... } })
        return ExtractRawJson(text, startIndex, toolNames);
    }

    private static ToolMatch? ExtractRawJson(string text, int startIndex, HashSet<string> toolNames)
    {
        int first = text.IndexOf('{', startIndex);
        if (first < 0) return null;

        int depth = 0;
        bool inStr = false, esc = false;
        for (int i = first; i < text.Length; i++)
        {
            char c = text[i];
            if (esc) { esc = false; continue; }
            if (inStr && c == '\\') { esc = true; continue; }
            if (c == '"') { inStr = !inStr; continue; }
            if (!inStr)
            {
                if (c == '{') depth++;
                else if (c == '}' && --depth == 0)
                    return ExtractJson(text[first..(i + 1)], toolNames) is { } tc ? new(tc, first, i - first + 1) : null;
            }
        }
        return null;
    }

    private static ToolCall? ExtractTag(string content, HashSet<string> names) =>
        ExtractJson(content, names) ?? ExtractXml(content, names);

    private static ToolCall? ExtractJson(string jsonStr, HashSet<string> names)
    {
        try
        {
            if (JsonNode.Parse(jsonStr) is JsonObject obj && (obj["name"] ?? obj["tool"])?.ToString() is { } name && names.Contains(name))
                return new ToolCall(Guid.NewGuid().ToString("N"), name, (obj["arguments"] ?? obj["parameters"]) as JsonObject ?? new());
        }
        catch { }
        return null;
    }

    private static ToolCall? ExtractXml(string content, HashSet<string> names)
    {
        if (XmlFuncPattern.Match(content) is not { Success: true } m || !names.Contains(m.Groups["name"].Value)) return null;

        var args = new JsonObject();
        foreach (Match p in XmlParamPattern.Matches(content))
        {
            var val = p.Groups["val"].Value.Trim();
            try { args[p.Groups["name"].Value] = JsonNode.Parse(val)?.DeepClone(); }
            catch { args[p.Groups["name"].Value] = val; }
        }
        return new ToolCall(Guid.NewGuid().ToString("N"), m.Groups["name"].Value, args);
    }
}
