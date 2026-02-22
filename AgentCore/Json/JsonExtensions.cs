using AgentCore.Chat;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AgentCore.Json;

public static class JsonExtensions
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    public static string NormalizeArgs(this JsonObject args) => Canonicalize(args).ToJsonString();

    private static JsonNode? Canonicalize(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => new KeyValuePair<string, JsonNode?>(p.Key, Canonicalize(p.Value)))),
        JsonArray arr => new JsonArray(arr.Select(Canonicalize).Where(n => n != null).Cast<JsonNode>().ToArray()),
        JsonValue val when val.TryGetValue(out string? s) => JsonValue.Create(Regex.Replace((s ?? "").Trim(), @"\s+", " ").ToLowerInvariant()),
        _ => node?.DeepClone()
    };

    public static string AsJsonString(this object? obj) => obj switch
    {
        null => string.Empty,
        string s => s,
        _ => JsonSerializer.Serialize(obj)
    };

    public static string AsPrettyJson(this object? content)
    {
        if (content == null) return "<empty>";
        if (content is JsonNode jn) return jn.ToJsonString(IndentedOptions);
        if (content is ToolCall tc && tc.Arguments != null) return tc.Arguments.ToJsonString(IndentedOptions);
        return JsonSerializer.Serialize(content, IndentedOptions) ?? "<unknown>";
    }

    public static bool TryParseCompleteJson(this string json, out JsonObject? result)
    {
        result = null;
        try { result = JsonNode.Parse(json)?.AsObject(); return result != null; }
        catch { return false; }
    }

    public static IEnumerable<(int Start, int End, JsonObject Obj)> FindAllJsonObjects(this string content)
    {
        var results = new List<(int, int, JsonObject)>();
        int depth = 0, start = -1;
        bool inString = false, escape = false;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];

            if (inString)
            {
                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') inString = false;
                continue;
            }

            if (c == '"') { inString = true; continue; }
            if (c == '{') { if (depth == 0) start = i; depth++; continue; }
            if (c == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    try { results.Add((start, i, JsonNode.Parse(content.Substring(start, i - start + 1))!.AsObject())); }
                    catch { }
                    start = -1;
                }
            }
        }

        return results;
    }
}
