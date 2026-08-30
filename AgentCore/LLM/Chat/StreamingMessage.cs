namespace AgentCore.LLM.Chat;

public static class MessageExtensions
{
    /// <summary>
    /// Wraps an LLM event stream into a streaming <see cref="Message"/>.
    /// </summary>
    public static Message ToMessage(this IAsyncEnumerable<IMessageEvent> stream)
        => new(stream);

    /// <summary>
    /// Consumes the entire stream and returns the finalized <see cref="Message"/>.
    /// </summary>
    public static async Task<Message> ToMessageAsync(
        this IAsyncEnumerable<IMessageEvent> stream,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var message = new Message(stream);
        await foreach (var _ in message.WithCancellation(ct).ConfigureAwait(false))
        {
            // Drain stream
        }
        return message;
    }
}
