using AgentCore.LLM;

namespace AgentCore.LLM.Chat;

/// <summary>
/// Dedicated state machine that accumulates streaming deltas into settled, validated <see cref="IContent"/> items.
/// </summary>
public interface IContentBuilder
{
    /// <summary>
    /// Determines whether the incoming delta belongs to the currently active logical content item(s) being assembled.
    /// </summary>
    /// <param name="delta">The incoming streaming delta.</param>
    /// <returns><c>true</c> if the delta continues the active logical content assembly; otherwise, <c>false</c>.</returns>
    bool CanAccept(IContentDelta delta);

    /// <summary>
    /// Appends the streaming delta to the internal assembly buffer.
    /// </summary>
    /// <param name="delta">The incoming streaming delta.</param>
    void Append(IContentDelta delta);

    /// <summary>
    /// Finalizes and materializes the accumulated state into settled, validated <see cref="IContent"/> item(s).
    /// </summary>
    /// <returns>The settled content items produced by this builder.</returns>
    IReadOnlyList<IContent> Build();
}
