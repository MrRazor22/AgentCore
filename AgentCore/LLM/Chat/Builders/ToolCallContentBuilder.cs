using System.Text;
using System.Text.Json.Nodes;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

public sealed class ToolCallContentBuilder : IContentBuilder
{
    private string _id = "";
    private readonly StringBuilder _name = new();
    private readonly StringBuilder _args = new();

    public IEnumerable<IContent> Append(IContentDelta delta)
    {
        if (delta is ToolCallDelta tcd)
        {
            if (!string.IsNullOrEmpty(tcd.Id))
            {
                _id = tcd.Id;
            }

            if (!string.IsNullOrEmpty(tcd.NameDelta))
            {
                var cur = _name.ToString();
                if (string.IsNullOrEmpty(cur) || (cur != tcd.NameDelta && !cur.EndsWith(tcd.NameDelta)))
                {
                    _name.Append(tcd.NameDelta);
                }
            }

            if (!string.IsNullOrEmpty(tcd.ArgumentsDelta))
            {
                _args.Append(tcd.ArgumentsDelta);
            }
        }

        if (delta.IsFinal)
        {
            var finalId = !string.IsNullOrEmpty(_id) ? _id : (delta.Id ?? "");
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
                Index = delta.Index,
                RawArguments = argsStr
            };
        }
    }
}














