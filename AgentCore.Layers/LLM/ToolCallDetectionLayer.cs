using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AgentCore.Layers.LLM;

public class ToolCallDetectionOptions
{
    public bool StopAfterFirstToolCall { get; set; } = false;
}

public class ToolCallDetectionLayer : LLMLayer
{
    private readonly ToolCallDetectionOptions _options;

    private static readonly Regex TagPattern = new Regex(
        @"[\[\(<](?<tag>[^\]\)>]*?tool[^\]\)>]*?)[\]\)>]\s*(?<content>[\s\S]*?)\s*[\[\(<]/\k<tag>[\]\)>]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline
    );

    private static readonly Regex MarkdownBlockPattern = new Regex(
        @"```json\s*(?<json>\{[\s\S]*?\})\s*```",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline
    );

    public ToolCallDetectionLayer(ToolCallDetectionOptions? options = null)
    {
        _options = options ?? new ToolCallDetectionOptions();
    }

    public override async IAsyncEnumerable<ILLMOutput> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var innerStream = Inner.StreamAsync(messages, responseSchema, tools, ct);
        var toolNames = tools?.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        if (toolNames.Count == 0)
        {
            await foreach (var item in innerStream.WithCancellation(ct).ConfigureAwait(false))
            {
                yield return item;
            }
            yield break;
        }

        Type? currentType = null;
        var segmentBuffer = new List<IContentDelta>();
        var totalToolCallsEmitted = 0;

        await foreach (var item in innerStream.WithCancellation(ct).ConfigureAwait(false))
        {
            var isTextOrReasoning = item is TextDelta or ReasoningDelta;
            var itemType = isTextOrReasoning ? item.GetType() : null;

            if (currentType != null && (itemType != currentType || !isTextOrReasoning))
            {
                foreach (var emitted in FlushSegment(segmentBuffer, currentType, toolNames))
                {
                    yield return emitted;
                    if (emitted is ToolCallDelta) totalToolCallsEmitted++;
                }
                segmentBuffer.Clear();
                currentType = null;

                if (_options.StopAfterFirstToolCall && totalToolCallsEmitted > 0 && isTextOrReasoning)
                {
                    yield break;
                }
            }

            if (isTextOrReasoning)
            {
                currentType = itemType;
                segmentBuffer.Add((IContentDelta)item);
            }
            else
            {
                yield return item;
                if (_options.StopAfterFirstToolCall && totalToolCallsEmitted > 0)
                {
                    yield break;
                }
            }
        }

        if (segmentBuffer.Count > 0)
        {
            foreach (var emitted in FlushSegment(segmentBuffer, currentType!, toolNames))
            {
                yield return emitted;
            }
        }
    }

    private static IEnumerable<ILLMOutput> FlushSegment(List<IContentDelta> buffer, Type type, HashSet<string> toolNames)
    {
        if (buffer.Count == 0) yield break;

        var combinedText = string.Concat(buffer.Select(GetDeltaText));
        int lastIndex = 0;

        while (lastIndex < combinedText.Length)
        {
            var parseResult = TryParseToolCall(combinedText.Substring(lastIndex), toolNames);
            if (parseResult.Success && parseResult.ToolCall != null)
            {
                if (parseResult.LeadingTextIndex > 0)
                {
                    var leadingText = combinedText.Substring(lastIndex, parseResult.LeadingTextIndex);
                    yield return CreateDelta(type, leadingText);
                }

                yield return parseResult.ToolCall;
                lastIndex += parseResult.MatchedEndIndex;
            }
            else
            {
                var remainingText = combinedText.Substring(lastIndex);
                if (!string.IsNullOrEmpty(remainingText))
                {
                    yield return CreateDelta(type, remainingText);
                }
                break;
            }
        }
    }

    private static string GetDeltaText(IContentDelta delta)
    {
        return delta switch
        {
            TextDelta td => td.Value,
            ReasoningDelta rd => rd.Thought,
            _ => ""
        };
    }

    private static IContentDelta CreateDelta(Type type, string text)
    {
        return type == typeof(ReasoningDelta) ? new ReasoningDelta(text) : new TextDelta(text);
    }

    private struct ParseResult
    {
        public bool Success;
        public bool IsDefiniteFailure;
        public ToolCallDelta? ToolCall;
        public int LeadingTextIndex;
        public int MatchedEndIndex;
    }

    private static int FindMatchingBrace(string text, int startIndex)
    {
        int braceCount = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = startIndex; i < text.Length; i++)
        {
            char c = text[i];
            if (escaped) { escaped = false; continue; }
            if (inString && c == '\\') { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }

            if (!inString)
            {
                if (c == '{') braceCount++;
                else if (c == '}')
                {
                    braceCount--;
                    if (braceCount == 0) return i;
                }
            }
        }
        return -1;
    }

    private static ParseResult TryParseToolCall(string text, HashSet<string> toolNames)
    {
        var tagMatch = TagPattern.Match(text);
        if (tagMatch.Success)
        {
            var innerContent = tagMatch.Groups["content"].Value;
            var toolCall = TryExtractFromJson(innerContent, toolNames) ?? TryExtractFromXmlTags(innerContent, toolNames);
            if (toolCall != null)
            {
                return new ParseResult
                {
                    Success = true,
                    ToolCall = toolCall,
                    LeadingTextIndex = tagMatch.Index,
                    MatchedEndIndex = tagMatch.Index + tagMatch.Length
                };
            }
        }

        var mdMatch = MarkdownBlockPattern.Match(text);
        if (mdMatch.Success)
        {
            var jsonStr = mdMatch.Groups["json"].Value;
            var toolCall = TryExtractFromJson(jsonStr, toolNames);
            if (toolCall != null)
            {
                return new ParseResult
                {
                    Success = true,
                    ToolCall = toolCall,
                    LeadingTextIndex = mdMatch.Index,
                    MatchedEndIndex = mdMatch.Index + mdMatch.Length
                };
            }
        }

        int firstBrace = text.IndexOf('{');
        if (firstBrace >= 0)
        {
            int lastBrace = FindMatchingBrace(text, firstBrace);
            if (lastBrace >= 0)
            {
                var jsonCandidate = text.Substring(firstBrace, lastBrace - firstBrace + 1);
                var toolCall = TryExtractFromJson(jsonCandidate, toolNames);
                if (toolCall != null)
                {
                    return new ParseResult
                    {
                        Success = true,
                        ToolCall = toolCall,
                        LeadingTextIndex = firstBrace,
                        MatchedEndIndex = lastBrace + 1
                    };
                }
                return new ParseResult { IsDefiniteFailure = true };
            }
        }

        if (text.Contains("</tool_call>") || text.Contains("[/TOOLCALL]") || text.Contains("```"))
        {
            return new ParseResult { IsDefiniteFailure = true };
        }
        return new ParseResult { Success = false };
    }

    private static ToolCallDelta? TryExtractFromJson(string jsonStr, HashSet<string> toolNames)
    {
        try
        {
            if (JsonNode.Parse(jsonStr) is JsonObject obj)
            {
                var name = (obj["name"] ?? obj["tool"])?.ToString();
                if (!string.IsNullOrEmpty(name) && toolNames.Contains(name))
                {
                    var args = (obj["arguments"] ?? obj["parameters"]) as JsonObject ?? new JsonObject();
                    return new ToolCallDelta(Guid.NewGuid().ToString(), name, args.ToJsonString());
                }
            }
        }
        catch { }
        return null;
    }

    private static ToolCallDelta? TryExtractFromXmlTags(string content, HashSet<string> toolNames)
    {
        var functionMatch = Regex.Match(content, @"(?i)<function\s*(?:=|\bname\s*=)\s*""?(?<name>[a-zA-Z0-9_\-]+)""?\s*>");
        if (!functionMatch.Success) return null;

        string funcName = functionMatch.Groups["name"].Value;
        if (!toolNames.Contains(funcName)) return null;

        var paramMatches = Regex.Matches(content, @"<parameter\s*=\s*""?(?<paramName>[a-zA-Z0-9_\-]+)""?\s*>(?<paramValue>[\s\S]*?)</parameter>", RegexOptions.IgnoreCase);
        var argsObj = new JsonObject();

        foreach (Match pm in paramMatches)
        {
            var pName = pm.Groups["paramName"].Value;
            var val = pm.Groups["paramValue"].Value.Trim();
            try { argsObj[pName] = JsonNode.Parse(val)?.DeepClone(); }
            catch { argsObj[pName] = val; }
        }

        return new ToolCallDelta(Guid.NewGuid().ToString(), funcName, argsObj.ToJsonString());
    }
}
