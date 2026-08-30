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

    public override Message StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        CancellationToken ct = default)
    {
        var inner = Inner.StreamAsync(messages, responseSchema, tools, ct);
        return new Message(ProcessContentsAsync(inner, tools, ct));
    }

    private async IAsyncEnumerable<IContent> ProcessContentsAsync(
        Message inner,
        IReadOnlyList<ToolDefinition>? tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var toolNames = tools?.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var totalToolCallsEmitted = 0;

        await foreach (var content in inner.ContentsStream(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (content is Text t && toolNames.Count > 0)
            {
                foreach (var parsed in ParseTextContent(t.Value, toolNames))
                {
                    yield return parsed;
                    if (parsed is ToolCall)
                    {
                        totalToolCallsEmitted++;
                        if (_options.StopAfterFirstToolCall)
                        {
                            yield break;
                        }
                    }
                }
            }
            else
            {
                yield return content;
                if (content is ToolCall && _options.StopAfterFirstToolCall)
                {
                    yield break;
                }
            }
        }
    }

    private static IEnumerable<IContent> ParseTextContent(string text, HashSet<string> toolNames)
    {
        int lastIndex = 0;
        while (lastIndex < text.Length)
        {
            var remaining = text.Substring(lastIndex);
            var parseResult = TryParseToolCall(remaining, toolNames);
            if (parseResult.Success && parseResult.ToolCall != null)
            {
                if (parseResult.LeadingTextIndex > 0)
                {
                    var leading = remaining.Substring(0, parseResult.LeadingTextIndex);
                    if (!string.IsNullOrEmpty(leading))
                    {
                        yield return new Text(leading);
                    }
                }

                yield return parseResult.ToolCall;
                lastIndex += parseResult.MatchedEndIndex;
            }
            else
            {
                var trailing = remaining;
                if (!string.IsNullOrEmpty(trailing))
                {
                    yield return new Text(trailing);
                }
                break;
            }
        }
    }

    private struct ParseResult
    {
        public bool Success;
        public ToolCall? ToolCall;
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
            }
        }

        return new ParseResult { Success = false };
    }

    private static ToolCall? TryExtractFromJson(string jsonStr, HashSet<string> toolNames)
    {
        try
        {
            if (JsonNode.Parse(jsonStr) is JsonObject obj)
            {
                var name = (obj["name"] ?? obj["tool"])?.ToString();
                if (!string.IsNullOrEmpty(name) && toolNames.Contains(name))
                {
                    var args = (obj["arguments"] ?? obj["parameters"]) as JsonObject ?? new JsonObject();
                    var callId = Guid.NewGuid().ToString("N");
                    return new ToolCall(callId, name, args);
                }
            }
        }
        catch { }
        return null;
    }

    private static ToolCall? TryExtractFromXmlTags(string content, HashSet<string> toolNames)
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

        var callId = Guid.NewGuid().ToString("N");
        return new ToolCall(callId, funcName, argsObj);
    }
}
