using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.LLM.Chat.Builders;

public sealed class ToolCallContentBuilder : IContentBuilder
{
    private class ToolCallState
    {
        public string Id { get; set; } = "";
        public int? Index { get; set; }
        public StringBuilder Name { get; } = new();
        public StringBuilder Args { get; } = new();
    }

    private readonly List<ToolCallState> _calls = new();

    public bool CanAccept(IContentDelta delta) => delta is ToolCallDelta;

    public void Append(IContentDelta delta)
    {
        if (delta is not ToolCallDelta tcd) return;

        ToolCallState? state = null;

        if (tcd.Index.HasValue)
        {
            state = _calls.FirstOrDefault(tc => tc.Index == tcd.Index.Value)
                 ?? _calls.FirstOrDefault(tc => tc.Id == tcd.Id && !string.IsNullOrEmpty(tcd.Id));
        }
        else if (!string.IsNullOrEmpty(tcd.Id))
        {
            state = _calls.FirstOrDefault(tc => tc.Id == tcd.Id);
        }

        if (state == null)
        {
            if (!tcd.Index.HasValue && string.IsNullOrEmpty(tcd.Id))
            {
                if (_calls.Count > 1)
                    throw new InvalidOperationException("Ambiguous tool call delta: multiple active tool calls exist.");
                state = _calls.Count == 1 ? _calls[0] : null;
            }

            if (state == null)
            {
                state = new ToolCallState
                {
                    Id = tcd.Id ?? "",
                    Index = tcd.Index
                };
                _calls.Add(state);
            }
        }

        if (!string.IsNullOrEmpty(tcd.Id) && state.Id != tcd.Id)
        {
            state.Id = tcd.Id;
        }

        if (!string.IsNullOrEmpty(tcd.NameDelta))
        {
            var cur = state.Name.ToString();
            if (string.IsNullOrEmpty(cur) || (cur != tcd.NameDelta && !cur.EndsWith(tcd.NameDelta)))
            {
                state.Name.Append(tcd.NameDelta);
            }
        }

        if (!string.IsNullOrEmpty(tcd.ArgumentsDelta))
        {
            state.Args.Append(tcd.ArgumentsDelta);
        }
    }

    public IReadOnlyList<IContent> Build()
    {
        var results = new List<IContent>();

        foreach (var call in _calls)
        {
            var name = call.Name.ToString().Trim();
            if (string.IsNullOrEmpty(name)) continue;

            var argsStr = call.Args.ToString().Trim();
            JsonObject? parsedArgs = null;
            if (!string.IsNullOrEmpty(argsStr))
            {
                try
                {
                    parsedArgs = JsonNode.Parse(argsStr)?.AsObject();
                }
                catch { }
            }

            results.Add(new ToolCall(call.Id, name, parsedArgs ?? new JsonObject())
            {
                Index = call.Index,
                RawArguments = argsStr
            });
        }

        return results;
    }
}
