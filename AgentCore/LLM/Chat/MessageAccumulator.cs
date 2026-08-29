using System.Runtime.CompilerServices;

namespace AgentCore.LLM.Chat;

/// <summary>
/// Routes and accumulates streaming lifecycle events across multiple active concurrent streams,
/// yielding settled <see cref="IContent"/> items as they complete and building a finalized <see cref="Message"/>.
/// </summary>
public sealed class MessageAccumulator
{
    private readonly TextAccumulator _text = new();
    private readonly ReasoningAccumulator _reasoning = new();
    private readonly ToolCallAccumulator _tools = new();
    private readonly List<IContent> _completedContents = [];

    private Role _role = Role.Assistant;
    private string? _id;
    private string? _model;
    private string? _finishReason;
    private TokenUsage? _usage;

    /// <summary>
    /// Ingests a streaming lifecycle event, routing by ID, and immediately returns completed <see cref="IContent"/> items.
    /// </summary>
    public IReadOnlyList<IContent> Receive(IMessageEvent output)
    {
        ArgumentNullException.ThrowIfNull(output);

        switch (output)
        {
            case MessageStart s:
                _role = s.Role;
                _id = s.Id;
                _model = s.Model;
                return [];

            case TextContentDelta d:         _text.Append(d.Text); return [];
            case TextContentEnd:             return AddAndReturn(_text.Complete());

            case ReasoningContentDelta d:    _reasoning.Append(d.Thought); return [];
            case ReasoningContentEnd:        return AddAndReturn(_reasoning.Complete());

            case ToolCallContentStart s:     _tools.Start(s.Id, s.Name, s.Index); return [];
            case ToolCallContentDelta d:     _tools.Append(d.Id, d.Arguments); return [];
            case ToolCallContentEnd e:       return AddAndReturn(_tools.Complete(e.Id));

            case MessageEnd e:
                if (e.FinishReason != null) _finishReason = e.FinishReason;
                if (e.Usage != null) _usage = e.Usage;
                return [];

            default:
                return [];
        }
    }

    private IReadOnlyList<IContent> AddAndReturn(IReadOnlyList<IContent> items)
    {
        if (items.Count > 0)
        {
            _completedContents.AddRange(items);
        }
        return items;
    }

    /// <summary>
    /// Constructs the final, completed <see cref="Message"/> with all settled contents and turn metadata.
    /// </summary>
    public Message ToMessage()
    {
        MessageMetadata? metadata = (_id != null || _model != null || _finishReason != null || _usage != null)
            ? new MessageMetadata(_id, _model, _finishReason, _usage)
            : null;

        return new Message(_role, _completedContents, metadata);
    }
}

public static class MessageAccumulatorExtensions
{
    /// <summary>
    /// Transforms a stream of raw lifecycle events into settled <see cref="IContent"/> items, accumulating into the provided accumulator.
    /// </summary>
    public static async IAsyncEnumerable<IContent> ToContentsAsync(
        this IAsyncEnumerable<IMessageEvent> stream,
        MessageAccumulator accumulator,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(accumulator);

        await foreach (var evt in stream.WithCancellation(ct).ConfigureAwait(false))
        {
            foreach (var content in accumulator.Receive(evt))
            {
                yield return content;
            }
        }
    }

    /// <summary>
    /// Consumes the entire stream of raw lifecycle events and returns the finalized <see cref="Message"/>.
    /// </summary>
    public static async Task<Message> ToMessageAsync(
        this IAsyncEnumerable<IMessageEvent> stream,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var accumulator = new MessageAccumulator();
        await foreach (var evt in stream.WithCancellation(ct).ConfigureAwait(false))
        {
            accumulator.Receive(evt);
        }
        return accumulator.ToMessage();
    }
}
