using AgentCore.Chat;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AgentCore.Json
{
    public static class JsonExtensions
    {
        public static string NormalizeArgs(this JObject args) =>
    Canonicalize(args).ToString(Formatting.None);

        private static JToken Canonicalize(JToken? node)
        {
            switch (node)
            {
                case JObject obj:
                    var newObj = new JObject();
                    foreach (var kvp in obj.Properties().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        newObj[kvp.Name] = Canonicalize(kvp.Value);
                    }
                    return newObj;

                case JArray arr:
                    var newArr = new JArray();
                    foreach (var item in arr)
                    {
                        newArr.Add(Canonicalize(item));
                    }
                    return newArr;

                case JValue val when val.Type == JTokenType.String:
                    var s = val.Value<string>() ?? "";
                    // trim, collapse multiple spaces, lowercase
                    s = Regex.Replace(s.Trim(), @"\s+", " ").ToLowerInvariant();
                    return JValue.CreateString(s);

                default:
                    return node?.DeepClone() ?? JValue.CreateNull();
            }
        }

        public static string AsJsonString(this object? obj)
        {
            if (obj == null) return "<null>";
            return obj is string s ? s : JsonConvert.SerializeObject(obj);
        }
        public static string AsPrettyJson(this object? content)
        {
            if (content == null)
                return "<empty>";

            // If it directly is a JToken payload (rare)
            if (content is JToken jt)
                return jt.ToString(Formatting.Indented);

            // If it's a ToolCall — pretty its arguments
            if (content is ToolCall tc)
            {
                if (tc.Arguments != null)
                    return tc.Arguments.ToString(Formatting.Indented);
            }

            // fallback: serialize the object
            var json = JsonConvert.SerializeObject(content, Formatting.Indented);
            return json ?? "<unknown>";
        }
        public static bool TryParseCompleteJson(this string json, out JObject? result)
        {
            result = null;
            try
            {
                result = JObject.Parse(json);
                return true;
            }
            catch
            {
                return false;
            }
        }
        public static IEnumerable<(int Start, int End, JObject Obj)> FindAllJsonObjects(this string content)
        {
            var results = new List<(int, int, JObject)>();

            int depth = 0;
            int start = -1;
            bool inString = false;
            bool escape = false;

            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];

                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escape = true;
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    if (depth == 0)
                        start = i;

                    depth++;
                    continue;
                }

                if (c == '}')
                {
                    depth--;

                    if (depth == 0 && start >= 0)
                    {
                        var jsonStr = content.Substring(start, i - start + 1);

                        try
                        {
                            var obj = JObject.Parse(jsonStr);
                            results.Add((start, i, obj));
                        }
                        catch { }

                        start = -1;
                    }
                }
            }

            return results;
        }
    }
}
