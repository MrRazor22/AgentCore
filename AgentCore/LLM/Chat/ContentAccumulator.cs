using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.LLM.Chat;

internal interface IContentAccumulator
{
    void Append(string chunk);
    IContent Complete();
}

internal sealed class TextAccumulator : IContentAccumulator
{
    private readonly StringBuilder _sb = new();

    public void Append(string chunk) => _sb.Append(chunk);

    public IContent Complete() => new Text(_sb.ToString());
}

internal sealed class ReasoningAccumulator : IContentAccumulator
{
    private readonly StringBuilder _sb = new();

    public void Append(string chunk) => _sb.Append(chunk);

    public IContent Complete() => new Reasoning(_sb.ToString());
}

internal sealed class ToolCallAccumulator(string id, string name, int index) : IContentAccumulator
{
    private readonly StringBuilder _args = new();

    public void Append(string chunk) => _args.Append(chunk);

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


