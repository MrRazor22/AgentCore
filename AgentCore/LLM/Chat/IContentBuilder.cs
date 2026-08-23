namespace AgentCore.LLM.Chat;

/// <summary>
/// Settles and streams completed <see cref="IContent"/> items from incoming stream chunks.
/// </summary>
public interface IContentBuilder
{
    /// <summary>
    /// Feeds the stream chunk into the builder and yields any completed <see cref="IContent"/> items.
    /// </summary>
    IEnumerable<IContent> Append(StreamChunk chunk);
}










