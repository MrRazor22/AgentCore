using System.Runtime.CompilerServices; 

namespace AgentCore.LLM.Chat
{

    public sealed class StreamingMessage : Message
    {
        private readonly IAsyncEnumerable<IMessageEvent> _eventStream;
        private int _consumed;

        public StreamingMessage(IAsyncEnumerable<IMessageEvent> stream) : base(Role.Assistant)
        {
            _eventStream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        /// <summary>
        /// Eagerly streams completed <see cref="IContent"/> blocks and seals the Message upon completion.
        /// </summary>
        public async IAsyncEnumerable<IContent> ContentsStream([EnumeratorCancellation] CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _consumed, 1) != 0)
            {
                throw new InvalidOperationException("A streaming Message can only be consumed once.");
            }

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
                    case TextStart s: active.TryAdd(s.Index, new Text.Accumulator()); break;
                    case TextDelta d: GetOrAdd(d.Index, () => new Text.Accumulator()).Append(d.Text); break;
                    case TextEnd e: if (Complete(e.Index) is { } t) yield return t; break;

                    // 2. Reasoning
                    case ReasoningStart s: active.TryAdd(s.Index, new Reasoning.Accumulator()); break;
                    case ReasoningDelta d: GetOrAdd(d.Index, () => new Reasoning.Accumulator()).Append(d.Thought); break;
                    case ReasoningEnd e: if (Complete(e.Index) is { } r) yield return r; break;

                    // 3. Tool Calls
                    case ToolCallStart s: active.TryAdd(s.Index, new ToolCall.Accumulator(s.Id, s.Name)); break;
                    case ToolCallDelta d: GetOrAdd(d.Index, () => new ToolCall.Accumulator(string.Empty, string.Empty)).Append(d.Arguments); break;
                    case ToolCallEnd e: if (Complete(e.Index) is { } call) yield return call; break;

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

            _contents.Clear();
            _contents.AddRange(completed.Values);

            if (id != null || model != null || finishReason != null || usage != null)
            {
                Metadata = new MessageMetadata(id, model, finishReason, usage);
            }

            T GetOrAdd<T>(int index, Func<T> factory) where T : class, IContentAccumulator
            {
                if (!active.TryGetValue(index, out var acc))
                {
                    active[index] = acc = factory();
                }
                else if (acc is not T)
                {
                    throw new InvalidOperationException($"Protocol violation: Stream block at index {index} is of type {acc.GetType().Name}, but received an event expecting {typeof(T).Name}.");
                }
                return (T)acc;
            }

            IContent? Complete(int index)
            {
                if (active.Remove(index, out var acc))
                {
                    var content = acc.Complete();
                    completed[index] = content;
                    return content;
                }
                return null;
            }
        }
    }
}
