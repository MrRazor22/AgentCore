using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace AgentCore.LLM.Chat;
public interface IStreamingContent : IContent
{
    IContent ToContent();
}
public sealed class StreamingMessage : Message, IAsyncEnumerable<IContent>
{
    private readonly IAsyncEnumerable<IMessageEvent> _stream;

    public StreamingMessage(IAsyncEnumerable<IMessageEvent> stream, Role role = Role.Assistant) : base(role)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Returns an immutable snapshot of the currently accumulated message state.
    /// </summary>
    public Message ToMessage() => new(Role, [.. _contents], Metadata);

    /// <summary>
    /// Directly streams contents (<see cref="StreamingText"/>, <see cref="StreamingReasoning"/>, and settled <see cref="ToolCall"/>s) as they become available.
    /// </summary>
    public async IAsyncEnumerator<IContent> GetAsyncEnumerator(CancellationToken ct = default)
    {
        var active = new Dictionary<int, (IStreamingContent Content, Action<IBlockDeltaEvent> Write, Action Complete)>();
        var completed = new SortedDictionary<int, IContent>();
        string? id = null, model = null, finishReason = null;
        TokenUsage? usage = null;

        bool success = false;
        try
        {
            await foreach (var evt in _stream.WithCancellation(ct).ConfigureAwait(false))
            {
                switch (evt)
                {
                    case MessageStart s:
                        Role = s.Role; id = s.Id; model = s.Model;
                        break;

                    case IBlockStartEvent s:
                    {
                        var ch = Channel.CreateUnbounded<IBlockDeltaEvent>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = false });
                        var deltas = ch.Reader.ReadAllAsync(ct);
                        var content = CreateStreamingContent(s, deltas);
                        active[s.Index] = (content, d => ch.Writer.TryWrite(d), () => ch.Writer.TryComplete());
                        if (content is not ToolCall)
                            yield return content;
                        break;
                    }

                    case IBlockDeltaEvent d:
                        if (!active.TryGetValue(d.Index, out var entry))
                            throw new InvalidOperationException($"Protocol violation: Received delta for index {d.Index} before a Start event.");
                        entry.Write(d);
                        break;

                    case IBlockEndEvent e:
                        if (!active.Remove(e.Index, out var endEntry))
                            throw new InvalidOperationException($"Protocol violation: Received End event for index {e.Index} before a Start event (or already ended).");
                        endEntry.Complete();
                        var materialized = endEntry.Content.ToContent();
                        completed[e.Index] = materialized;
                        if (materialized is ToolCall tc)
                        {
                            yield return tc;
                        }
                        break;

                    case MessageEnd end:
                        finishReason = end.FinishReason;
                        usage = end.Usage;
                        break;
                }
            }

            // Gracefully complete any unclosed active blocks
            foreach (var (idx, entry) in active.OrderBy(x => x.Key))
            {
                entry.Complete();
                var materialized = entry.Content.ToContent();
                completed[idx] = materialized;
                if (materialized is ToolCall tc)
                {
                    yield return tc;
                }
            }

            _contents.AddRange(completed.Values);
            Metadata = [new MessageMetadata(id, model, finishReason, usage)];
            success = true;
        }
        finally
        {
            if (!success)
            {
                foreach (var entry in active.Values)
                    entry.Complete();
            }
        }
    }

    private static IStreamingContent CreateStreamingContent(IBlockStartEvent start, IAsyncEnumerable<IBlockDeltaEvent> deltas) => start switch
    {
        TextStart => new StreamingText(Filter<TextDelta>(deltas)),
        ReasoningStart => new StreamingReasoning(Filter<ReasoningDelta>(deltas)),
        ToolCallStart t => new StreamingToolCall(t.Id, t.Name, Filter<ToolCallDelta>(deltas)),
        _ => throw new NotSupportedException($"Unsupported start event: {start.GetType().Name}")
    };

    private static async IAsyncEnumerable<T> Filter<T>(IAsyncEnumerable<IBlockDeltaEvent> source, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
        {
            if (item is T typed)
                yield return typed;
        }
    }

    /// <summary>
    /// Asynchronously drains the stream to completion and returns this message with fully populated contents and metadata.
    /// </summary>
    public async Task<Message> ToMessageAsync(CancellationToken ct = default)
    {
        await foreach (var _ in this.WithCancellation(ct).ConfigureAwait(false)) { }
        return this;
    }
}