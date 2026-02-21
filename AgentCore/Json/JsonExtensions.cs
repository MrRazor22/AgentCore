using AgentCore.Chat;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace AgentCore.Json;

public static class JsonExtensions
{
    public static string NormalizeArgs(this JObject args) => Canonicalize(args).ToString(Formatting.None);

    private static JToken Canonicalize(JToken? node) => node switch
    {
        JObject obj => new JObject(obj.Properties().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p => new JProperty(p.Name, Canonicalize(p.Value)))),
        JArray arr => new JArray(arr.Select(Canonicalize)),
        JValue val when val.Type == JTokenType.String => JValue.CreateString(Regex.Replace((val.Value<string>() ?? "").Trim(), @"\s+", " ").ToLowerInvariant()),
        _ => node?.DeepClone() ?? JValue.CreateNull()
    };

    public static string AsJsonString(this object? obj) => obj switch
    {
        null => string.Empty,
        string s => s,
        _ => JsonConvert.SerializeObject(obj, Formatting.None)
    };

    public static string AsPrettyJson(this object? content)
    {
        if (content == null) return "<empty>";
        if (content is JToken jt) return jt.ToString(Formatting.Indented);
        if (content is ToolCall tc && tc.Arguments != null) return tc.Arguments.ToString(Formatting.Indented);
        return JsonConvert.SerializeObject(content, Formatting.Indented) ?? "<unknown>";
    }

    public static bool TryParseCompleteJson(this string json, out JObject? result)
    {
        result = null;
        try { result = JObject.Parse(json); return true; }
        catch { return false; }
    }

    public static IEnumerable<(int Start, int End, JObject Obj)> FindAllJsonObjects(this string content)
    {
        var results = new List<(int, int, JObject)>();
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
                    try { results.Add((start, i, JObject.Parse(content.Substring(start, i - start + 1)))); }
                    catch { }
                    start = -1;
                }
            }
        }

        return results;
    }
}
