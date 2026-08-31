using System.Runtime.CompilerServices; 

namespace AgentCore.LLM.Chat
{

    public sealed class StreamingMessage : Message
    {
        private readonly IAsyncEnumerable<IMessageEvent> _eventStream;

        public StreamingMessage(IAsyncEnumerable<IMessageEvent> stream) : base(Role.Assistant)
        {
            _eventStream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        /// <summary>
        /// Eagerly streams completed <see cref="IContent"/> blocks and seals the Message upon completion.
        /// </summary>
        public async IAsyncEnumerable<IContent> ContentsStream([EnumeratorCancellation] CancellationToken ct = default)
        {
            var active = new Dictionary<int, IStreamingContent>();
            var completed = new SortedDictionary<int, IContent>();
            string? id = null, model = null, finishReason = null;
            TokenUsage? usage = null;

            await foreach (var evt in _eventStream.WithCancellation(ct).ConfigureAwait(false))
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

                    case IDataDeltaEvent<string> d:
                        if (!active.TryGetValue(d.Index, out var stream))
                            throw new InvalidOperationException($"Protocol violation: Received delta for index {d.Index} before a Start event.");
                        if (stream is not IStreamingContent<string> textStream)
                            throw new InvalidOperationException($"Protocol violation: Stream block at index {d.Index} is of type {stream.GetType().Name}, but received an event expecting {nameof(IStreamingContent<string>)}.");
                        textStream.Append(d.Data);
                        break;

                    case IBlockEndEvent e:
                        if (!active.Remove(e.Index, out var endStream))
                            throw new InvalidOperationException($"Protocol violation: Received End event for index {e.Index} before a Start event (or already ended).");
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
            foreach (var (idx, stream) in active.OrderBy(x => x.Key))
            {
                var content = stream.ToContent();
                completed[idx] = content;
                yield return content;
            }
             
            _contents.AddRange(completed.Values);
            Metadata = new MessageMetadata(id, model, finishReason, usage);
        }

        /// <summary>
        /// Asynchronously drains the stream to completion and returns this message with fully populated contents and metadata.
        /// </summary>
        public async Task<Message> ToMessageAsync(CancellationToken ct = default)
        {
            await foreach (var _ in ContentsStream(ct).ConfigureAwait(false)) { }
            return this;
        }
    }
}
