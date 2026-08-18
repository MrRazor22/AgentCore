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
        public bool Emitted { get; set; }
    }

    private readonly List<ToolCallState> _calls = new();

    public bool CanHandle(IContentDelta delta) => delta is ToolCallDelta;

    public IEnumerable<IContent> Append(IContentDelta contentDelta)
    {
        if (contentDelta is not ToolCallDelta delta) yield break;

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

        // Check if any tool call reached complete JSON and can be settled immediately
        foreach (var call in _calls)
        {
            if (call.Emitted) continue;
            var name = call.Name.ToString().Trim();
            if (string.IsNullOrEmpty(name)) continue;

            var argsStr = call.Args.ToString().Trim();
            if (argsStr.Length > 0 && argsStr.StartsWith('{') && argsStr.EndsWith('}'))
            {
                JsonObject? parsed = null;
                try
                {
                    parsed = JsonNode.Parse(argsStr)?.AsObject();
                }
                catch { }

                if (parsed != null)
                {
                    call.Emitted = true;
                    yield return new ToolCall(call.Id, name, parsed)
                    {
                        Index = call.Index,
                        RawArguments = argsStr
                    };
                }
            }
        }
    }
}







