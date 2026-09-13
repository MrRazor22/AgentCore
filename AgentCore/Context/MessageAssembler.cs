using AgentCore.LLM.Chat;
using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.Context;

internal sealed class MessageAssembler(Role role, string? id = null, IReadOnlyList<IMetadata>? metadata = null)
{
    private readonly SortedDictionary<int, (IContentStart Start, StringBuilder Buffer, List<IContent> Chunks)> _blocks = [];
    private readonly List<IContent> _contents = [];
    private readonly List<IMetadata> _metadata = metadata != null ? [.. metadata] : [];

    public Role Role { get; private set; } = role;
    public string? Id { get; private set; } = id;
    public IReadOnlyList<IMetadata> Metadata => _metadata;

    public void Push(IMessageEvent evt)
    {
        switch (evt)
        {
            case MessageStart s:
                Role = s.Role;
                Id = s.MessageId;
                _metadata.Clear();
                break;

            case MetadataEvent m:
                _metadata.Add(m.Metadata);
                break;

            case ContentEvent cb:
                _contents.Add(cb.Content);
                break;

            case IContentStart s:
                _blocks[s.Index] = (s, new StringBuilder(), []);
                break;

            case TextDelta d when _blocks.TryGetValue(d.Index, out var b):
                b.Buffer.Append(d.Text);
                break;

            case ReasoningDelta d when _blocks.TryGetValue(d.Index, out var b):
                b.Buffer.Append(d.Thought);
                break;

            case ToolCallDelta d when _blocks.TryGetValue(d.Index, out var b):
                b.Buffer.Append(d.Arguments);
                break;

            case IContentEnd end:
                CompleteBlock(end.Index);
                break;

            case MessageEnd:
                CompleteAllBlocks();
                break;
        }
    }

    public Message ToSnapshot()
    {
        var snapshotContents = new List<IContent>(_contents);
        foreach (var b in _blocks.Values)
        {
            snapshotContents.Add(CreateContent(b.Start, b.Buffer.ToString(), b.Chunks));
        }
        return BuildMessage(snapshotContents);
    }

    public Message ToMessage()
    {
        CompleteAllBlocks();
        return BuildMessage(_contents);
    }

    private Message BuildMessage(List<IContent> contents)
    {
        var msgContents = contents.Count > 0 ? contents : [new Text(string.Empty)];
        return new Message(Role, msgContents, Id, _metadata);
    }

    private void CompleteAllBlocks()
    {
        foreach (var index in _blocks.Keys.ToList())
        {
            CompleteBlock(index);
        }
    }

    private void CompleteBlock(int index)
    {
        if (!_blocks.Remove(index, out var b)) return;
        _contents.Add(CreateContent(b.Start, b.Buffer.ToString(), b.Chunks));
    }

    private static IContent CreateContent(IContentStart start, string text, List<IContent> chunks) => start switch
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
