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
    }
}
