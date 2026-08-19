namespace AgentCore.LLM.Chat;

/// <summary>
/// Streams completed <see cref="IContent"/> items asynchronously from incoming deltas.
/// </summary>
public interface IContentBuilder
{
    /// <summary>
    /// Feeds the delta into the builder and streams any completed <see cref="IContent"/> items asynchronously.
    /// </summary>
    IAsyncEnumerable<IContent> AppendAsync(IContentDelta delta, CancellationToken ct = default);
}









