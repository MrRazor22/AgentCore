using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Json;

public static class JsonExtensions
{

    public static string AsJsonString(this object? obj) => obj switch
    {
        null => string.Empty,
        string s => s,
        _ => JsonSerializer.Serialize(obj)
    };



    public static bool TryParseCompleteJson(this string json, out JsonObject? result)
    {
        result = null;
        try { result = JsonNode.Parse(json)?.AsObject(); return result != null; }
        catch { return false; }
    }

}
