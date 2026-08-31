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
            var active = new Dictionary<int, IContentAccumulator>();
            var completed = new SortedDictionary<int, IContent>();
            string? id = null, model = null, finishReason = null;
            TokenUsage? usage = null;

            await foreach (var evt in _eventStream.WithCancellation(ct).ConfigureAwait(false))
            {
                switch (evt)
                {
                    case MessageStart s:
                        Role = s.Role; id = s.Id; model = s.Model; break;

                    // 1. Text
                    case TextStart s: AddStart(s.Index, new Text.Accumulator()); break;
                    case TextDelta d: GetRequired<Text.Accumulator>(d.Index).Append(d.Text); break;
                    case TextEnd e: yield return Complete<Text.Accumulator>(e.Index); break;

                    // 2. Reasoning
                    case ReasoningStart s: AddStart(s.Index, new Reasoning.Accumulator()); break;
                    case ReasoningDelta d: GetRequired<Reasoning.Accumulator>(d.Index).Append(d.Thought); break;
                    case ReasoningEnd e: yield return Complete<Reasoning.Accumulator>(e.Index); break;

                    // 3. Tool Calls
                    case ToolCallStart s: AddStart(s.Index, new ToolCall.Accumulator(s.Id, s.Name)); break;
                    case ToolCallDelta d: GetRequired<ToolCall.Accumulator>(d.Index).Append(d.Arguments); break;
                    case ToolCallEnd e: yield return Complete<ToolCall.Accumulator>(e.Index); break;

                    // 4. End
                    case MessageEnd end:
                        finishReason = end.FinishReason;
                        usage = end.Usage;
                        break;
                }
            }

            // Gracefully complete and yield any remaining unclosed blocks
            foreach (var (idx, acc) in active.OrderBy(x => x.Key))
            {
                var content = acc.Complete();
                completed[idx] = content;
                yield return content;
            }
             
            _contents.AddRange(completed.Values);

            Metadata = new MessageMetadata(id, model, finishReason, usage);

            void AddStart(int index, IContentAccumulator accumulator)
            {
                if (!active.TryAdd(index, accumulator))
                    throw new InvalidOperationException($"Protocol violation: Block already started at index {index}.");
            }

            T GetRequired<T>(int index) where T : class, IContentAccumulator
            {
                if (!active.TryGetValue(index, out var acc))
                    throw new InvalidOperationException($"Protocol violation: Received delta for index {index} before a Start event.");
                if (acc is not T typedAcc)
                    throw new InvalidOperationException($"Protocol violation: Stream block at index {index} is of type {acc.GetType().Name}, but received an event expecting {typeof(T).Name}.");
                return typedAcc;
            }

            IContent Complete<T>(int index) where T : class, IContentAccumulator
            {
                if (!active.Remove(index, out var acc))
                    throw new InvalidOperationException($"Protocol violation: Received End event for index {index} before a Start event (or already ended).");
                if (acc is not T)
                    throw new InvalidOperationException($"Protocol violation: End event type mismatch at index {index}. Expected {typeof(T).Name} accumulator, but found {acc.GetType().Name}.");
                
                var content = acc.Complete();
                completed[index] = content;
                return content;
            }
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
