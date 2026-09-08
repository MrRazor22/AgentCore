using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace AgentCore.LLM.Chat;

public interface IStreamingContent : IContent
{
    void Complete();
    IContent ToContent();
}

public sealed class StreamingText : Text, IStreamingContent, IAsyncEnumerable<TextDelta>
{
    private readonly StringBuilder _sb = new();
    private readonly Channel<TextDelta> _channel = Channel.CreateUnbounded<TextDelta>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = false });

    public StreamingText() : base("") { }

    public StreamingText(IAsyncEnumerable<TextDelta> stream) : base("")
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var delta in stream.ConfigureAwait(false))
                {
                    Append(delta);
                }
            }
            finally
            {
                Complete();
            }
        });
    }

    public override string Value => _sb.ToString();

    public void Append(TextDelta delta)
    {
        _sb.Append(delta.Text);
        _channel.Writer.TryWrite(delta);
    }

    public void Complete() => _channel.Writer.TryComplete();

    public IAsyncEnumerator<TextDelta> GetAsyncEnumerator(CancellationToken ct = default)
        => _channel.Reader.ReadAllAsync(ct).GetAsyncEnumerator(ct);

    public Text ToContent() => new(Value);
    IContent IStreamingContent.ToContent() => ToContent();
}

public sealed class StreamingReasoning : Reasoning, IStreamingContent, IAsyncEnumerable<ReasoningDelta>
{
    private readonly StringBuilder _sb = new();
    private readonly Channel<ReasoningDelta> _channel = Channel.CreateUnbounded<ReasoningDelta>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = false });

    public StreamingReasoning() : base("") { }

    public StreamingReasoning(IAsyncEnumerable<ReasoningDelta> stream) : base("")
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var delta in stream.ConfigureAwait(false))
                {
                    Append(delta);
                }
            }
            finally
            {
                Complete();
            }
        });
    }

    public override string Value => _sb.ToString();

    public void Append(ReasoningDelta delta)
    {
        _sb.Append(delta.Thought);
        _channel.Writer.TryWrite(delta);
    }

    public void Complete() => _channel.Writer.TryComplete();

    public IAsyncEnumerator<ReasoningDelta> GetAsyncEnumerator(CancellationToken ct = default)
        => _channel.Reader.ReadAllAsync(ct).GetAsyncEnumerator(ct);

    public Reasoning ToContent() => new(Value);
    IContent IStreamingContent.ToContent() => ToContent();
}

public sealed class StreamingToolCall : ToolCall, IStreamingContent, IAsyncEnumerable<ToolCallDelta>
{
    private readonly StringBuilder _args = new();
    private readonly Channel<ToolCallDelta> _channel = Channel.CreateUnbounded<ToolCallDelta>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = false });

    public StreamingToolCall(string id, string name) : base(id, name, new JsonObject()) { }

    public StreamingToolCall(string id, string name, IAsyncEnumerable<ToolCallDelta> stream) : base(id, name, new JsonObject())
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var delta in stream.ConfigureAwait(false))
                {
                    Append(delta);
                }
            }
            finally
            {
                Complete();
            }
        });
    }

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

    public void Append(ToolCallDelta delta)
    {
        _args.Append(delta.Arguments);
        _channel.Writer.TryWrite(delta);
    }

    public void Complete() => _channel.Writer.TryComplete();

    public IAsyncEnumerator<ToolCallDelta> GetAsyncEnumerator(CancellationToken ct = default)
        => _channel.Reader.ReadAllAsync(ct).GetAsyncEnumerator(ct);

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