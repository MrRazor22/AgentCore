using System.Text;
using System.Text.Json.Nodes;
using AgentCore.LLM;

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

    public bool TryAppend(IContentDelta contentDelta)
    {
        if (contentDelta is not ToolCallDelta delta) return false;
        ToolCallState? state = null;

        if (delta.Index.HasValue)
        {
            state = _calls.FirstOrDefault(tc => tc.Index == delta.Index.Value)
                 ?? _calls.FirstOrDefault(tc => tc.Id == delta.Id && !string.IsNullOrEmpty(delta.Id));
        }
        else if (!string.IsNullOrEmpty(delta.Id))
        {
            state = _calls.FirstOrDefault(tc => tc.Id == delta.Id);
        }

        if (state == null)
        {
            if (!delta.Index.HasValue && string.IsNullOrEmpty(delta.Id))
            {
                if (_calls.Count > 1)
                    throw new InvalidOperationException("Ambiguous tool call delta: multiple active tool calls exist.");
                state = _calls.Count == 1 ? _calls[0] : null;
            }

            if (state == null)
            {
                state = new ToolCallState
                {
                    Id = delta.Id ?? "",
                    Index = delta.Index
                };
                _calls.Add(state);
            }
        }

        if (!string.IsNullOrEmpty(delta.Id) && state.Id != delta.Id)
        {
            state.Id = delta.Id;
        }

        if (!string.IsNullOrEmpty(delta.NameDelta))
        {
            var cur = state.Name.ToString();
            if (string.IsNullOrEmpty(cur) || (cur != delta.NameDelta && !cur.EndsWith(delta.NameDelta)))
            {
                state.Name.Append(delta.NameDelta);
            }
        }

        if (!string.IsNullOrEmpty(delta.ArgumentsDelta))
        {
            state.Args.Append(delta.ArgumentsDelta);
        }

        return true;
    }

    public IReadOnlyList<IContent> ToContents()
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
