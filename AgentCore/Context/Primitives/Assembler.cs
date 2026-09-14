using AgentCore.LLM.Chat;
using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.Context.Primitives;

public interface IAssembler
{
    IAssembler Create(MessageStart? start = null);
    IContent? Push(MessageDelta delta);
    Message ToMessage(MessageEnd? end = null);
}

public sealed class Assembler : IAssembler
{
    private readonly SortedDictionary<int, (IContentStart Start, StringBuilder Buffer)> _blocks = [];
    private readonly List<IContent> _contents = [];
    private readonly List<IMetadata> _metadata = [];
    private Role _role = Role.Assistant;
    private string? _id;

    public IAssembler Create(MessageStart? start = null) => new Assembler
    {
        _role = start?.Role ?? Role.Assistant,
        _id = start?.Id
    };

    public IContent? Push(MessageDelta delta)
    {
        if (delta.Metadata != null) _metadata.Add(delta.Metadata);
        if (delta.Content is null) return null;

        switch (delta.Content)
        {
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

            default:
                return null;
        }
    }

    public Message ToMessage(MessageEnd? end = null)
    {
        if (end != null)
        {
            if (end.Id != null) _id ??= end.Id;
            CompleteAllBlocks();
            return new Message(_role, _contents, _id, _metadata);
        }

        var snapshotContents = new List<IContent>(_contents);
        foreach (var b in _blocks.Values)
        {
            if (b.Start is not ToolCallStart ? b.Buffer.Length > 0 : IsValidJson(b.Buffer.ToString()))
                snapshotContents.Add(CreateContent(b.Start, b.Buffer.ToString()));
        }
        return new Message(_role, snapshotContents, _id, _metadata);
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

    private static bool IsValidJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return true;
        try { return JsonNode.Parse(raw) is JsonObject; }
        catch { return false; }
    }

    private static JsonObject ParseArgs(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new JsonObject();
        try { return JsonNode.Parse(raw)?.AsObject() ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }
}

public static class AssemblerExtensions
{
    public static async Task<IReadOnlyList<Message>> ToMessagesAsync(
        this IAsyncEnumerable<IMessageEvent> stream,
        IAssembler? assembler = null,
        CancellationToken ct = default)
    {
        var factory = assembler ?? new Assembler();
        var open = new Dictionary<string, IAssembler>(StringComparer.Ordinal);
        var messages = new List<Message>();

        await foreach (var e in stream.WithCancellation(ct).ConfigureAwait(false))
        {
            if (e is Message m) { messages.Add(m); continue; }
            var key = e.Id ?? string.Empty;
            if (e is MessageStart ms) open[key] = factory.Create(ms);
            else if (e is MessageDelta md && open.TryGetValue(key, out var asm)) asm.Push(md);
            else if (e is MessageEnd me && open.Remove(key, out var endAsm)) messages.Add(endAsm.ToMessage(me));
        }

        foreach (var remaining in open.Values)
            if (remaining.ToMessage() is { Contents.Count: > 0 } partial) messages.Add(partial);

        return messages;
    }
}

