using System.Text;
using System.Text.Json.Nodes;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

public sealed class ToolCallContentBuilder : IContentBuilder
{
    private string _id = "";
    private readonly StringBuilder _name = new();
    private readonly StringBuilder _args = new();

    public IEnumerable<IContent> Append(StreamChunk chunk)
    {
        if (!string.IsNullOrEmpty(chunk.Id))
        {
            _id = chunk.Id;
        }

        if (chunk.Content is ToolCallChunk tcd)
        {
            if (!string.IsNullOrEmpty(tcd.Name))
            {
                var cur = _name.ToString();
                if (string.IsNullOrEmpty(cur) || (cur != tcd.Name && !cur.EndsWith(tcd.Name)))
                {
                    _name.Append(tcd.Name);
                }
            }

            if (!string.IsNullOrEmpty(tcd.Arguments))
            {
                _args.Append(tcd.Arguments);
            }
        }

        if (chunk.IsFinal)
        {
            var finalId = !string.IsNullOrEmpty(_id) ? _id : (chunk.Id ?? "");
            var name = _name.ToString().Trim();
            var argsStr = _args.ToString().Trim();

            JsonObject? parsed = null;
            if (!string.IsNullOrEmpty(argsStr))
            {
                try
                {
                    parsed = JsonNode.Parse(argsStr)?.AsObject();
                }
                catch { }
            }

            yield return new ToolCall(finalId, name, parsed ?? new JsonObject())
            {
                Index = chunk.Index,
                RawArguments = argsStr
            };
        }
    }
}














