using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace AgentCore.LLM.Chat;

public sealed class StreamingMessage : Message, IAsyncEnumerable<IContent>
{
    private readonly IAsyncEnumerable<IMessageEvent> _stream;

    private record ActiveBlock(
        IStreamingContent Content,
        Action<IBlockDeltaEvent> OnDelta,
        Action<Exception?> OnComplete);

    public StreamingMessage(IAsyncEnumerable<IMessageEvent> stream, Role role = Role.Assistant) : base(role)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Returns an immutable snapshot of the currently accumulated message state.
    /// </summary>
    public Message ToMessage() => new(Role, _contents, Metadata);

    /// <summary>
    /// Directly enumerates completed <see cref="IContent"/> blocks from the underlying message event stream.
    /// </summary>
    public async IAsyncEnumerator<IContent> GetAsyncEnumerator(CancellationToken ct = default)
    {
        var active = new Dictionary<int, ActiveBlock>();
        var completed = new SortedDictionary<int, IContent>();
        string? id = null, model = null, finishReason = null;
        TokenUsage? usage = null;

        var enumerator = _stream.WithCancellation(ct).ConfigureAwait(false).GetAsyncEnumerator();
        try
        {
            while (true)
            {
                IMessageEvent evt;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                        break;
                    evt = enumerator.Current;
                }
                catch (Exception ex)
                {
                    foreach (var block in active.Values)
                        block.OnComplete(ex);
                    throw;
                }

                switch (evt)
                {
                    case MessageStart s:
                        Role = s.Role; id = s.Id; model = s.Model;
                        break;

                    case TextStart s:
                    {
                        var ch = Channel.CreateUnbounded<TextDelta>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = false });
                        var sb = new StringBuilder();
                        var text = new StreamingText(ch.Reader, sb);
                        if (!active.TryAdd(s.Index, new(text, d => { sb.Append(((TextDelta)d).Text); ch.Writer.TryWrite((TextDelta)d); }, ex => ch.Writer.TryComplete(ex))))
                            throw new InvalidOperationException($"Protocol violation: Block already started at index {s.Index}.");
                        break;
                    }

                    case ReasoningStart s:
                    {
                        var ch = Channel.CreateUnbounded<ReasoningDelta>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = false });
                        var sb = new StringBuilder();
                        var reasoning = new StreamingReasoning(ch.Reader, sb);
                        if (!active.TryAdd(s.Index, new(reasoning, d => { sb.Append(((ReasoningDelta)d).Thought); ch.Writer.TryWrite((ReasoningDelta)d); }, ex => ch.Writer.TryComplete(ex))))
                            throw new InvalidOperationException($"Protocol violation: Block already started at index {s.Index}.");
                        break;
                    }

                    case ToolCallStart s:
                    {
                        var ch = Channel.CreateUnbounded<ToolCallDelta>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = false });
                        var args = new StringBuilder();
                        var toolCall = new StreamingToolCall(s.Id, s.Name, ch.Reader, args);
                        if (!active.TryAdd(s.Index, new(toolCall, d => { args.Append(((ToolCallDelta)d).Arguments); ch.Writer.TryWrite((ToolCallDelta)d); }, ex => ch.Writer.TryComplete(ex))))
                            throw new InvalidOperationException($"Protocol violation: Block already started at index {s.Index}.");
                        break;
                    }

                    case IBlockDeltaEvent d:
                        if (!active.TryGetValue(d.Index, out var block))
                            throw new InvalidOperationException($"Protocol violation: Received delta for index {d.Index} before a Start event.");
                        block.OnDelta(d);
                        break;

                    case IBlockEndEvent e:
                        if (!active.Remove(e.Index, out var endBlock))
                            throw new InvalidOperationException($"Protocol violation: Received End event for index {e.Index} before a Start event (or already ended).");
                        endBlock.OnComplete(null);
                        var content = endBlock.Content.ToContent();
                        completed[e.Index] = content;
                        yield return content;
                        break;

                    case MessageEnd end:
                        finishReason = end.FinishReason;
                        usage = end.Usage;
                        break;
                }
            }

            // Gracefully complete and yield any remaining unclosed blocks
            foreach (var (idx, activeBlock) in active.OrderBy(x => x.Key))
            {
                activeBlock.OnComplete(null);
                var content = activeBlock.Content.ToContent();
                completed[idx] = content;
                yield return content;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        _contents.AddRange(completed.Values);
        Metadata = new MessageMetadata(id, model, finishReason, usage);
    }

    /// <summary>
    /// Asynchronously drains the stream to completion and returns this message with fully populated contents and metadata.
    /// </summary>
    public async Task<Message> ToMessageAsync(CancellationToken ct = default)
    {
        await foreach (var _ in this.WithCancellation(ct).ConfigureAwait(false)) { }
        return ToMessage();
    }
}
 