using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.LLM.Chat;

internal interface IContentAccumulator
{
    IContent Complete();
}

internal sealed class TextAccumulator : IContentAccumulator
{
    private readonly StringBuilder _sb = new();

    public void Append(string text) => _sb.Append(text);

    public IContent Complete() => new Text(_sb.ToString());
}

internal sealed class ReasoningAccumulator : IContentAccumulator
{
    private readonly StringBuilder _sb = new();

    public void Append(string thought) => _sb.Append(thought);

    public IContent Complete() => new Reasoning(_sb.ToString());
}

internal sealed class ToolCallAccumulator(string id, string name, int index) : IContentAccumulator
{
    private readonly StringBuilder _args = new();

    public void Append(string arguments) => _args.Append(arguments);

    public IContent Complete()
    {
        var raw = _args.ToString();
        JsonObject? args = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                args = JsonNode.Parse(raw)?.AsObject();
            }
            catch (Exception ex)
            {
                throw new FormatException($"Malformed JSON arguments for tool '{name}' (id: '{id}'): {raw}", ex);
            }
        }

        return new ToolCall(id, name, args ?? new JsonObject())
        {
            RawArguments = raw,
            Index = index
        };
    }
}

