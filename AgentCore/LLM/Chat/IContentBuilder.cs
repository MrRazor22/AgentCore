namespace AgentCore.LLM.Chat;

/// <summary>
/// Settles and streams completed <see cref="IContent"/> items from incoming deltas.
/// </summary>
public interface IContentBuilder
{
    /// <summary>
    /// Feeds the delta into the builder and yields any completed <see cref="IContent"/> items.
    /// </summary>
    IEnumerable<IContent> Append(IContentDelta delta);
}










