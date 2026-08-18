namespace AgentCore.LLM.Chat;

/// <summary>
/// Settles and streams <see cref="IContent"/> items from incoming deltas.
/// </summary>
public interface IContentBuilder
{
    /// <summary>
    /// Checks whether this builder handles the incoming delta type.
    /// </summary>
    bool CanHandle(IContentDelta delta);

    /// <summary>
    /// Feeds the delta into the builder and immediately yields any settled <see cref="IContent"/> items.
    /// </summary>
    IEnumerable<IContent> Append(IContentDelta delta);
}



