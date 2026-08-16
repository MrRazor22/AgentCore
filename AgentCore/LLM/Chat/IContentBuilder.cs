namespace AgentCore.LLM.Chat;

/// <summary>
/// Settles and materializes accumulated streaming content into <see cref="IContent"/> items.
/// </summary>
public interface IContentBuilder
{
    /// <summary>
    /// Attempts to append the streaming delta if it belongs to this builder.
    /// </summary>
    /// <param name="delta">The incoming streaming delta.</param>
    /// <returns><c>true</c> if the delta was accepted and appended; otherwise, <c>false</c>.</returns>
    bool TryAppend(IContentDelta delta);

    /// <summary>
    /// Materializes the builder's current state into a list of <see cref="IContent"/> items.
    /// </summary>
    IReadOnlyList<IContent> ToContents();
}
