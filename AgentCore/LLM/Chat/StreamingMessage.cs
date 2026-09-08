namespace AgentCore.LLM.Chat;

public sealed class StreamingMessage(IAsyncEnumerable<IMessageEvent> stream, Role role = Role.Assistant) 
    : Message(role), IAsyncEnumerable<IContent>
{
    /// <summary>
    /// Returns an immutable snapshot of the currently accumulated message state.
    /// </summary>
    public Message ToMessage() => new(Role, [.. _contents], Metadata);

    /// <summary>
    /// Directly streams contents (<see cref="StreamingText"/>, <see cref="StreamingReasoning"/>, and settled <see cref="ToolCall"/>s) as they become available.
    /// </summary>
    public async IAsyncEnumerator<IContent> GetAsyncEnumerator(CancellationToken ct = default)
    {
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

                case TextStart s:
                    var text = new StreamingText();
                    active[s.Index] = text;
                    yield return text;
                    break;

                case ReasoningStart s:
                    var reasoning = new StreamingReasoning();
                    active[s.Index] = reasoning;
                    yield return reasoning;
                    break;

                case ToolCallStart s:
                    active[s.Index] = new StreamingToolCall(s.Id, s.Name);
                    break;

                case TextDelta d:
                    if (!active.TryGetValue(d.Index, out var tc))
                        throw new InvalidOperationException($"Protocol violation: Received delta for index {d.Index} before a Start event.");
                    ((StreamingText)tc).Append(d);
                    break;

                case ReasoningDelta d:
                    if (!active.TryGetValue(d.Index, out var rc))
                        throw new InvalidOperationException($"Protocol violation: Received delta for index {d.Index} before a Start event.");
                    ((StreamingReasoning)rc).Append(d);
                    break;

                case ToolCallDelta d:
                    if (!active.TryGetValue(d.Index, out var toolc))
                        throw new InvalidOperationException($"Protocol violation: Received delta for index {d.Index} before a Start event.");
                    ((StreamingToolCall)toolc).Append(d);
                    break;

                case IBlockEndEvent e:
                    if (!active.Remove(e.Index, out var endEntry))
                        throw new InvalidOperationException($"Protocol violation: Received End event for index {e.Index} before a Start event (or already ended).");
                    endEntry.Complete();
                    var materialized = endEntry.ToContent();
                    completed[e.Index] = materialized;
                    if (materialized is ToolCall tool)
                    {
                        yield return tool;
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
            var materialized = entry.ToContent();
            completed[idx] = materialized;
            if (materialized is ToolCall tool)
            {
                yield return tool;
            }
        }

        _contents.AddRange(completed.Values);
        Metadata = [new MessageMetadata(id, model, finishReason, usage)];
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