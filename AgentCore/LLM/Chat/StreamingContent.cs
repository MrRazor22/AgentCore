using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace AgentCore.LLM.Chat;

public interface IStreamingContent : IContent
{
    void Receive(IBlockDeltaEvent delta);
    void Complete();
    IContent ToContent();
}

public interface IStreamingContent<out TContent> : IStreamingContent where TContent : IContent
{
    new TContent ToContent();
}

public sealed class StreamingText : IStreamingContent<Text>, IAsyncEnumerable<TextDelta>
{
    private readonly StringBuilder _sb = new();
    private readonly Channel<TextDelta> _channel = Channel.CreateUnbounded<TextDelta>(new UnboundedChannelOptions
    {
        SingleWriter = true,
        SingleReader = false
    });

    public void Receive(IBlockDeltaEvent delta)
    {
        if (delta is not TextDelta text)
            throw new InvalidOperationException($"Protocol violation: StreamingText expected {nameof(TextDelta)} but received {delta.GetType().Name}.");
        _sb.Append(text.Text);
        _channel.Writer.TryWrite(text);
    }

    public void Complete() => _channel.Writer.TryComplete();

    public IAsyncEnumerator<TextDelta> GetAsyncEnumerator(CancellationToken ct = default)
        => _channel.Reader.ReadAllAsync(ct).GetAsyncEnumerator(ct);

    public Text ToContent() => new(_sb.ToString());
    IContent IStreamingContent.ToContent() => ToContent();

    public int EstimateTokens() => (int)Math.Ceiling(_sb.Length / 4.0);
    public IContent Truncate(int maxTokens, string? notice = null) => ToContent().Truncate(maxTokens, notice);
    public override string ToString() => _sb.ToString();
}

public sealed class StreamingReasoning : IStreamingContent<Reasoning>, IAsyncEnumerable<ReasoningDelta>
{
    private readonly StringBuilder _sb = new();
    private readonly Channel<ReasoningDelta> _channel = Channel.CreateUnbounded<ReasoningDelta>(new UnboundedChannelOptions
    {
        SingleWriter = true,
        SingleReader = false
    });

    public void Receive(IBlockDeltaEvent delta)
    {
        if (delta is not ReasoningDelta reasoning)
            throw new InvalidOperationException($"Protocol violation: StreamingReasoning expected {nameof(ReasoningDelta)} but received {delta.GetType().Name}.");
        _sb.Append(reasoning.Thought);
        _channel.Writer.TryWrite(reasoning);
    }

    public void Complete() => _channel.Writer.TryComplete();

    public IAsyncEnumerator<ReasoningDelta> GetAsyncEnumerator(CancellationToken ct = default)
        => _channel.Reader.ReadAllAsync(ct).GetAsyncEnumerator(ct);

    public Reasoning ToContent() => new(_sb.ToString());
    IContent IStreamingContent.ToContent() => ToContent();

    public int EstimateTokens() => (int)Math.Ceiling(_sb.Length / 4.0);
    public IContent Truncate(int maxTokens, string? notice = null) => ToContent().Truncate(maxTokens, notice);
    public override string ToString() => _sb.ToString();
}

public sealed class StreamingToolCall(string id, string name) : IStreamingContent<ToolCall>, IAsyncEnumerable<ToolCallDelta>
{
    private readonly StringBuilder _args = new();
    private readonly Channel<ToolCallDelta> _channel = Channel.CreateUnbounded<ToolCallDelta>(new UnboundedChannelOptions
    {
        SingleWriter = true,
        SingleReader = false
    });

    public string Id => id;
    public string Name => name;

    public void Receive(IBlockDeltaEvent delta)
    {
        if (delta is not ToolCallDelta toolCall)
            throw new InvalidOperationException($"Protocol violation: StreamingToolCall expected {nameof(ToolCallDelta)} but received {delta.GetType().Name}.");
        _args.Append(toolCall.Arguments);
        _channel.Writer.TryWrite(toolCall);
    }

    public void Complete() => _channel.Writer.TryComplete();

    public IAsyncEnumerator<ToolCallDelta> GetAsyncEnumerator(CancellationToken ct = default)
        => _channel.Reader.ReadAllAsync(ct).GetAsyncEnumerator(ct);

    public ToolCall ToContent()
    {
        var raw = _args.ToString();
        JsonObject? args = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                args = JsonNode.Parse(raw)?.AsObject();
            }
            catch (Exception ex)
            {
                throw new FormatException($"Malformed JSON arguments for tool '{name}' (id: '{id}'): {raw}", ex);
            }
        }

        return new ToolCall(id, name, args ?? new JsonObject());
    }

    IContent IStreamingContent.ToContent() => ToContent();

    public int EstimateTokens() => (int)Math.Ceiling((name.Length + _args.Length) / 4.0);
    public IContent Truncate(int maxTokens, string? notice = null) => ToContent().Truncate(maxTokens, notice);
    public override string ToString() => $"{name}({_args})";
}