using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace AgentCore.LLM.Chat;

public sealed class StreamingText(IAsyncEnumerable<TextDelta> stream) : Text(""), IStreamingContent, IAsyncEnumerable<TextDelta>
{
    private readonly StringBuilder _sb = new();

    public override string Value => _sb.ToString();

    public async IAsyncEnumerator<TextDelta> GetAsyncEnumerator(CancellationToken ct = default)
    {
        await foreach (var delta in stream.WithCancellation(ct).ConfigureAwait(false))
        {
            _sb.Append(delta.Text);
            yield return delta;
        }
    }

    public Text ToContent() => new(Value);
    IContent IStreamingContent.ToContent() => ToContent();
}

public sealed class StreamingReasoning(IAsyncEnumerable<ReasoningDelta> stream) : Reasoning(""), IStreamingContent, IAsyncEnumerable<ReasoningDelta>
{
    private readonly StringBuilder _sb = new();

    public override string Value => _sb.ToString();

    public async IAsyncEnumerator<ReasoningDelta> GetAsyncEnumerator(CancellationToken ct = default)
    {
        await foreach (var delta in stream.WithCancellation(ct).ConfigureAwait(false))
        {
            _sb.Append(delta.Thought);
            yield return delta;
        }
    }

    public Reasoning ToContent() => new(Value);
    IContent IStreamingContent.ToContent() => ToContent();
}

public sealed class StreamingToolCall(string id, string name, IAsyncEnumerable<ToolCallDelta> stream) : ToolCall(id, name, new JsonObject()), IStreamingContent, IAsyncEnumerable<ToolCallDelta>
{
    private readonly StringBuilder _args = new();

    public override JsonObject Arguments
    {
        get
        {
            var raw = _args.ToString();
            if (string.IsNullOrWhiteSpace(raw)) return new JsonObject();
            try
            {
                return JsonNode.Parse(raw)?.AsObject() ?? new JsonObject();
            }
            catch
            {
                return new JsonObject();
            }
        }
    }

    public async IAsyncEnumerator<ToolCallDelta> GetAsyncEnumerator(CancellationToken ct = default)
    {
        await foreach (var delta in stream.WithCancellation(ct).ConfigureAwait(false))
        {
            _args.Append(delta.Arguments);
            yield return delta;
        }
    }

    public ToolCall ToContent()
    {
        var raw = _args.ToString();
        JsonObject? parsedArgs = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                parsedArgs = JsonNode.Parse(raw)?.AsObject();
            }
            catch (Exception parseEx)
            {
                throw new FormatException($"Malformed JSON arguments for tool '{Name}' (id: '{Id}'): {raw}", parseEx);
            }
        }

        return new ToolCall(Id, Name, parsedArgs ?? new JsonObject());
    }

    IContent IStreamingContent.ToContent() => ToContent();
}