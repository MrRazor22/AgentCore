using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CodeSharp.Layers;

public class StreamingToolCallParserOptions
{
    public bool StopAfterFirstToolCall { get; set; } = false;
}

public class StreamingToolCallParserLayer : LLMLayer
{
    private readonly StreamingToolCallParserOptions _options;

    // Matches any tag like <tool_call>...</tool_call>, [TOOLCALL]...[/TOOLCALL], etc.
    // Specifically looking for open tag enclosing JSON, followed by corresponding closing tag.
    private static readonly Regex TagPattern = new Regex(
        @"^[\[\(<](?<tag>[^\]\)>]*?tool[^\]\)>]*?)[\]\)>]\s*(?<json>\{[\s\S]*?\})\s*[\[\(<]/\k<tag>[\]\)>]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline
    );

    // Matches markdown json block: ```json ... ```
    private static readonly Regex MarkdownBlockPattern = new Regex(
        @"^```json\s*(?<json>\{[\s\S]*?\})\s*```",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline
    );

    public StreamingToolCallParserLayer(ILLM inner, StreamingToolCallParserOptions? options = null) : base(inner)
    {
        _options = options ?? new StreamingToolCallParserOptions();
    }

    public override async IAsyncEnumerable<ILLMOutput> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var innerStream = Inner.StreamAsync(messages, options, tools, ct);
        var toolNames = tools?.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();

        if (toolNames.Count == 0)
        {
            // No tools registered, bypass completely
            await foreach (var item in innerStream.WithCancellation(ct).ConfigureAwait(false))
            {
                yield return item;
            }
            yield break;
        }

        var deltaBuffer = new List<IContentDelta>();
        var isBuffering = false;
        var totalToolCallsEmitted = 0;

        await foreach (var item in innerStream.WithCancellation(ct).ConfigureAwait(false))
        {
            if (item is ToolCallDelta)
            {
                // Native ToolCallDeltas pass through untouched
                yield return item;
                totalToolCallsEmitted++;
                continue;
            }

            if (item is not IContentDelta contentDelta)
            {
                yield return item;
                continue;
            }

            var chunk = GetDeltaText(contentDelta);
            if (string.IsNullOrEmpty(chunk))
            {
                continue;
            }

            if (!isBuffering)
            {
                if (HasTriggerMarker(chunk))
                {
                    isBuffering = true;
                    deltaBuffer.Add(contentDelta);
                }
                else
                {
                    yield return item;
                }
            }
            else
            {
                deltaBuffer.Add(contentDelta);
            }

            if (isBuffering)
            {
                bool processedToolCall;
                do
                {
                    processedToolCall = false;
                    var fullBufferedText = string.Concat(deltaBuffer.Select(GetDeltaText));
                    var parseResult = TryParseToolCall(fullBufferedText, toolNames);

                    if (parseResult.Success && parseResult.ToolCall != null)
                    {
                        // Yield any leading text before the tool call start
                        if (parseResult.LeadingTextIndex > 0)
                        {
                            var (leading, _) = SplitBuffer(deltaBuffer, parseResult.LeadingTextIndex);
                            foreach (var l in leading)
                            {
                                yield return l;
                            }
                        }

                        // Yield the parsed tool call
                        yield return parseResult.ToolCall;
                        totalToolCallsEmitted++;

                        // Consume the matched portion from the buffer
                        var (_, remaining) = SplitBuffer(deltaBuffer, parseResult.MatchedEndIndex);
                        deltaBuffer.Clear();
                        deltaBuffer.AddRange(remaining);

                        processedToolCall = true;

                        if (_options.StopAfterFirstToolCall && totalToolCallsEmitted > 0)
                        {
                            yield break;
                        }
                    }
                    else if (parseResult.IsDefiniteFailure)
                    {
                        // Flush the buffer and reset buffering state
                        foreach (var d in deltaBuffer)
                        {
                            yield return d;
                        }
                        deltaBuffer.Clear();
                        isBuffering = false;
                        break;
                    }
                    else
                    {
                        // To prevent unbounded buffering, flush if it grows too large
                        if (fullBufferedText.Length > 8192)
                        {
                            foreach (var d in deltaBuffer)
                            {
                                yield return d;
                            }
                            deltaBuffer.Clear();
                            isBuffering = false;
                        }
                        break;
                    }
                } while (processedToolCall && deltaBuffer.Count > 0);

                if (deltaBuffer.Count > 0 && !isBuffering)
                {
                    foreach (var d in deltaBuffer)
                    {
                        yield return d;
                    }
                    deltaBuffer.Clear();
                }
            }
        }

        // End of stream cleanup: flush any remaining buffered candidate
        if (deltaBuffer.Count > 0)
        {
            var finalBufferedText = string.Concat(deltaBuffer.Select(GetDeltaText));
            var finalParseResult = TryParseToolCall(finalBufferedText, toolNames);
            if (finalParseResult.Success && finalParseResult.ToolCall != null)
            {
                if (finalParseResult.LeadingTextIndex > 0)
                {
                    var (leading, _) = SplitBuffer(deltaBuffer, finalParseResult.LeadingTextIndex);
                    foreach (var l in leading)
                    {
                        yield return l;
                    }
                }
                yield return finalParseResult.ToolCall;
            }
            else
            {
                foreach (var d in deltaBuffer)
                {
                    yield return d;
                }
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

    private static (List<IContentDelta> Leading, List<IContentDelta> Remaining) SplitBuffer(List<IContentDelta> buffer, int splitIndex)
    {
        var leading = new List<IContentDelta>();
        var remaining = new List<IContentDelta>();
        int currentIndex = 0;

        foreach (var delta in buffer)
        {
            var text = GetDeltaText(delta);
            if (currentIndex + text.Length <= splitIndex)
            {
                leading.Add(delta);
                currentIndex += text.Length;
            }
            else if (currentIndex < splitIndex)
            {
                int take = splitIndex - currentIndex;
                var pref = text.Substring(0, take);
                var suff = text.Substring(take);

                if (delta is TextDelta)
                {
                    leading.Add(new TextDelta(pref));
                    if (!string.IsNullOrEmpty(suff)) remaining.Add(new TextDelta(suff));
                }
                else
                {
                    leading.Add(new ReasoningDelta(pref));
                    if (!string.IsNullOrEmpty(suff)) remaining.Add(new ReasoningDelta(suff));
                }
                currentIndex = splitIndex;
            }
            else
            {
                remaining.Add(delta);
            }
        }

        return (leading, remaining);
    }

    private static bool HasTriggerMarker(string text)
    {
        if (text.Contains("<tool", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("<function", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("<call", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("[TOOL", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("[CALL", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("```json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        int braceIdx = text.IndexOf('{');
        if (braceIdx >= 0 && IsPotentialJsonCandidate(text, braceIdx))
        {
            return true;
        }

        return false;
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

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inString && c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (!inString)
            {
                if (c == '{')
                {
                    braceCount++;
                }
                else if (c == '}')
                {
                    braceCount--;
                    if (braceCount == 0)
                    {
                        return i;
                    }
                }
            }
        }

        return -1;
    }

    private static bool IsPotentialJsonCandidate(string text, int startIndex)
    {
        int idx = startIndex + 1;
        while (idx < text.Length && char.IsWhiteSpace(text[idx]))
        {
            idx++;
        }

        if (idx >= text.Length)
        {
            return true; // Still waiting for property characters, keep buffering
        }

        var remaining = text.Substring(idx);
        
        // Match partial starts of '"name"' or '"tool"'
        if ("\"name\"".StartsWith(remaining, StringComparison.OrdinalIgnoreCase) ||
            "\"tool\"".StartsWith(remaining, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (remaining.StartsWith("\"name\"", StringComparison.OrdinalIgnoreCase) ||
            remaining.StartsWith("\"tool\"", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static ParseResult TryParseToolCall(string text, HashSet<string> toolNames)
    {
        // 1. Check for XML/Bracket tag structures
        int openTagStart = -1;
        int openTagEnd = -1;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '<' || text[i] == '[')
            {
                openTagStart = i;
            }
            else if (openTagStart >= 0 && (text[i] == '>' || text[i] == ']'))
            {
                openTagEnd = i;
                var rawTagName = text.Substring(openTagStart + 1, openTagEnd - openTagStart - 1).Trim();
                var spaceIdx = rawTagName.IndexOf(' ');
                var tagName = spaceIdx >= 0 ? rawTagName.Substring(0, spaceIdx) : rawTagName;

                if (tagName.Contains("tool", StringComparison.OrdinalIgnoreCase))
                {
                    var closingTag = (text[openTagStart] == '<') ? $"</{tagName}>" : $"[/{tagName}]";
                    var closeTagIdx = text.IndexOf(closingTag, openTagEnd + 1, StringComparison.OrdinalIgnoreCase);
                    
                    if (closeTagIdx >= 0)
                    {
                        var matchedEndIdx = closeTagIdx + closingTag.Length;
                        var innerContent = text.Substring(openTagEnd + 1, closeTagIdx - openTagEnd - 1);

                        // Try JSON extraction first
                        var toolCall = TryExtractFromJson(innerContent, toolNames);
                        if (toolCall == null)
                        {
                            // Try XML function/parameter tags extraction
                            toolCall = TryExtractFromXmlTags(innerContent, toolNames);
                        }

                        if (toolCall != null)
                        {
                            return new ParseResult
                            {
                                Success = true,
                                ToolCall = toolCall,
                                LeadingTextIndex = openTagStart,
                                MatchedEndIndex = matchedEndIdx
                            };
                        }
                    }
                }
                openTagStart = -1;
            }
        }

        // 2. Check for markdown json block
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

        // 3. Raw JSON brace matching (using string-aware matcher)
        int firstBrace = text.IndexOf('{');
        if (firstBrace >= 0)
        {
            // Verify if it looks like a valid tool call property shape: {"name" or {"tool"
            if (!IsPotentialJsonCandidate(text, firstBrace))
            {
                return new ParseResult { IsDefiniteFailure = true };
            }

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
                else
                {
                    // Found a complete brace-matched JSON object but it doesn't contain a registered tool
                    return new ParseResult { IsDefiniteFailure = true };
                }
            }
        }

        // Check if the current buffer is invalid/definite failure
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
            var node = JsonNode.Parse(jsonStr);
            if (node is JsonObject obj)
            {
                string? name = null;
                if (obj.TryGetPropertyValue("name", out var nameNode) && nameNode != null)
                {
                    name = nameNode.ToString();
                }
                else if (obj.TryGetPropertyValue("tool", out var toolNode) && toolNode != null)
                {
                    name = toolNode.ToString();
                }

                if (!string.IsNullOrEmpty(name) && toolNames.Contains(name))
                {
                    JsonObject? args = null;
                    if (obj.TryGetPropertyValue("arguments", out var argsNode) && argsNode is JsonObject argsObj)
                    {
                        args = argsObj;
                    }
                    else if (obj.TryGetPropertyValue("parameters", out var paramsNode) && paramsNode is JsonObject paramsObj)
                    {
                        args = paramsObj;
                    }
                    else
                    {
                        args = new JsonObject();
                    }

                    return new ToolCallDelta(
                        Id: Guid.NewGuid().ToString(),
                        NameDelta: name,
                        ArgumentsDelta: args.ToJsonString()
                    );
                }
            }
        }
        catch
        {
            // Parse failed, ignore
        }

        return null;
    }

    private static ToolCallDelta? TryExtractFromXmlTags(string content, HashSet<string> toolNames)
    {
        var functionMatch = Regex.Match(content, @"<function\s*=\s*""?(?<name>[a-zA-Z0-9_\-]+)""?\s*>", RegexOptions.IgnoreCase);
        if (!functionMatch.Success)
        {
            functionMatch = Regex.Match(content, @"<function\s+name\s*=\s*""(?<name>[a-zA-Z0-9_\-]+)""\s*>", RegexOptions.IgnoreCase);
        }

        if (!functionMatch.Success) return null;

        string funcName = functionMatch.Groups["name"].Value;
        if (!toolNames.Contains(funcName)) return null;

        var paramPattern = @"<parameter\s*=\s*""?(?<paramName>[a-zA-Z0-9_\-]+)""?\s*>(?<paramValue>[\s\S]*?)</parameter>";
        var paramMatches = Regex.Matches(content, paramPattern, RegexOptions.IgnoreCase);
        
        var argsObj = new JsonObject();

        foreach (Match pm in paramMatches)
        {
            var paramName = pm.Groups["paramName"].Value;
            var paramValueStr = pm.Groups["paramValue"].Value.Trim();

            try
            {
                var node = JsonNode.Parse(paramValueStr);
                argsObj[paramName] = node?.DeepClone();
            }
            catch
            {
                argsObj[paramName] = paramValueStr;
            }
        }

        return new ToolCallDelta(
            Id: Guid.NewGuid().ToString(),
            NameDelta: funcName,
            ArgumentsDelta: argsObj.ToJsonString()
        );
    }
}
