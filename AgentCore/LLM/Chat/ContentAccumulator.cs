using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.LLM.Chat;

internal sealed class TextAccumulator
{
    private readonly StringBuilder _sb = new();
    public void Append(string? text) => _sb.Append(text);
    public IReadOnlyList<IContent> Complete() => _sb.Length > 0 ? [new Text(_sb.ToString())] : [];
}

internal sealed class ReasoningAccumulator
{
    private readonly StringBuilder _sb = new();
    public void Append(string? thought) => _sb.Append(thought);
    public IReadOnlyList<IContent> Complete() => _sb.Length > 0 ? [new Reasoning(_sb.ToString())] : [];
}

internal sealed class ToolCallAccumulator
{
    private readonly Dictionary<string, (string Name, StringBuilder Args, int? Index)> _calls = new();

    public void Start(string id, string name, int? index) => _calls[id] = (name, new StringBuilder(), index);
    public void Append(string id, string? args) { if (_calls.TryGetValue(id, out var tc)) tc.Args.Append(args); }

    public IReadOnlyList<IContent> Complete(string id)
    {
        if (!_calls.Remove(id, out var call)) return [];

        var raw = call.Args.ToString().Trim();
        JsonObject? parsed = null;
        if (raw.Length > 0)
        {
            try { parsed = JsonNode.Parse(raw)?.AsObject(); } catch { }
        }

        return [new ToolCall(id, call.Name, parsed ?? new JsonObject()) { Index = call.Index, RawArguments = raw }];
    }
}
