using AgentCore.Chat;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace AgentCore.Utils
{
    public static class Helpers
    {
        public static string ToJoinedString<T>(
            this IEnumerable<T> source,
            string separator = "\n")
        {
            if (source == null) return "<null>";
            var list = source.ToList();
            return list.Count > 0
                ? string.Join(separator, list.Select(x => x?.ToString()))
                : "<empty>";
        }
        public static string ToSnake(this string s)
        {
            return string.Concat(
                s.Select((c, i) =>
                    i > 0 && char.IsUpper(c)
                        ? "_" + char.ToLowerInvariant(c)
                        : char.ToLowerInvariant(c).ToString()
                )
            );
        }

    }
}
