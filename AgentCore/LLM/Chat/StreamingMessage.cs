using System.Runtime.CompilerServices; 

namespace AgentCore.LLM.Chat;

public sealed class StreamingMessage : Message
{
    public StreamingMessage(Role role = Role.Assistant) : base(role)
    {
    }

    /// <summary>
    /// Eagerly streams completed <see cref="IContent"/> blocks and seals the Message upon completion.
    /// </summary>
    public async IAsyncEnumerable<IContent> Receive(
        IAsyncEnumerable<IMessageEvent> stream,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var active = new Dictionary<int, IStreamingContent>();
        var completed = new SortedDictionary<int, IContent>();
        string? id = null, model = null, finishReason = null;
        TokenUsage? usage = null;

        await foreach (var evt in stream.WithCancellation(ct).ConfigureAwait(false))
        {
            switch (evt)
            {
                case MessageStart s:
                    Role = s.Role; id = s.Id; model = s.Model;
                    break;

                case IBlockStartEvent s:
                    if (!active.TryAdd(s.Index, s.CreateStream()))
                        throw new InvalidOperationException($"Protocol violation: Block already started at index {s.Index}.");
                    break;

                case IBlockDeltaEvent d:
                    if (!active.TryGetValue(d.Index, out var blockStream))
                        throw new InvalidOperationException($"Protocol violation: Received delta for index {d.Index} before a Start event.");
                    blockStream.Receive(d);
                    break;

                case IBlockEndEvent e:
                    if (!active.Remove(e.Index, out var endStream))
                        throw new InvalidOperationException($"Protocol violation: Received End event for index {e.Index} before a Start event (or already ended).");
                    endStream.Complete();
                    var content = endStream.ToContent();
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
        foreach (var (idx, activeStream) in active.OrderBy(x => x.Key))
        {
            activeStream.Complete();
            var content = activeStream.ToContent();
            completed[idx] = content;
            yield return content;
        }

        _contents.AddRange(completed.Values);
        Metadata = new MessageMetadata(id, model, finishReason, usage);
    }

    /// <summary>
    /// Asynchronously drains the stream to completion and returns this message with fully populated contents and metadata.
    /// </summary>
    public async Task<Message> ToMessageAsync(
        IAsyncEnumerable<IMessageEvent> stream,
        CancellationToken ct = default)
    {
        await foreach (var _ in Receive(stream, ct).ConfigureAwait(false)) { }
        return this;
    }
}
 