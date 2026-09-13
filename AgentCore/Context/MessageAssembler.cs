using AgentCore.LLM.Chat;
using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.Context;

public interface IMessageAssembler
{
    IContent? Push(IMessageEvent evt);
    Message ToMessage();
}

public sealed class MessageAssembler(Role role = Role.Assistant, string? id = null) : IMessageAssembler
{
    private readonly SortedDictionary<int, (IContentStart Start, StringBuilder Buffer)> _blocks = [];
    private readonly List<IContent> _contents = [];
    private readonly List<IMetadata> _metadata = [];
    private Role _role = role;
    private string? _id = id;

    public IContent? Push(IMessageEvent evt)
    {
        switch (evt)
        {
            case MessageStart s:
                _role = s.Role;
                _id = s.Id;
                return null;

            case MessageDelta d:
                if (d.Metadata != null) _metadata.Add(d.Metadata);
                return d.Content != null ? Push(d.Content) : null;

            case IContent c:
                _contents.Add(c);
                return c;

            case IContentStart s:
                _blocks[s.Index] = (s, new StringBuilder());
                return null;

            case TextDelta d when _blocks.TryGetValue(d.Index, out var b):
                b.Buffer.Append(d.Text);
                return null;

            case ReasoningDelta d when _blocks.TryGetValue(d.Index, out var b):
                b.Buffer.Append(d.Thought);
                return null;

            case ToolCallDelta d when _blocks.TryGetValue(d.Index, out var b):
                b.Buffer.Append(d.Arguments);
                return null;

            case IContentEnd end:
                return CompleteBlock(end.Index);

            case MessageEnd:
                CompleteAllBlocks();
                return null;

            default:
                return null;
        }
    }

    public Message ToMessage()
    {
        var contents = new List<IContent>(_contents);
        foreach (var b in _blocks.Values)
        {
            contents.Add(CreateContent(b.Start, b.Buffer.ToString()));
        }
        var msgContents = contents.Count > 0 ? contents : [new Text(string.Empty)];
        return new Message(_role, msgContents, _id, _metadata);
    }

    private void CompleteAllBlocks()
    {
        foreach (var index in _blocks.Keys.ToList())
        {
            CompleteBlock(index);
        }
    }

    private IContent? CompleteBlock(int index)
    {
        if (!_blocks.Remove(index, out var b)) return null;
        var content = CreateContent(b.Start, b.Buffer.ToString());
        _contents.Add(content);
        return content;
    }

    private static IContent CreateContent(IContentStart start, string text) => start switch
    {
        TextStart => new Text(text),
        ReasoningStart => new Reasoning(text),
        ToolCallStart tc => new ToolCall(tc.Id, tc.Name, ParseArgs(text)),
        _ => new Text(text)
    };

    private static JsonObject ParseArgs(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new JsonObject();
        try { return JsonNode.Parse(raw)?.AsObject() ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }
}
