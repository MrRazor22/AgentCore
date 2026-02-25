using AgentCore.Chat;
using AgentCore.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Tools;

public interface IToolCallParser
{
    ToolCall? TryMatch(string content);
}

public sealed class ToolCallParser(IToolCatalog _toolCatalog) : IToolCallParser
{
    public ToolCall? TryMatch(string content)
    {
        foreach (var (start, _, obj) in content.FindAllJsonObjects())
        {
            var name = obj["name"]?.ToString();
            var args = obj["arguments"] as JsonObject;
            if (name == null || args == null || !_toolCatalog.Contains(name)) continue;

            var id = obj["id"]?.ToString() ?? Guid.NewGuid().ToString();
            var prefix = start > 0 ? content.Substring(0, start) : null;

            return new ToolCall(id, name, args);
        }
        return null;
    }
}
