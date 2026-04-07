using AgentCore.Conversation;

namespace AgentCore.Context;

/// <summary>
/// A source of context that gets assembled into the agent's context window.
/// </summary>
public interface IContextSource
{
    /// <summary>Unique name for this source (e.g., "rules", "persona", "workflow").</summary>
    string Name { get; }

    /// <summary>Which role this context should appear as (System, User, etc.).</summary>
    Role Role { get; }

    /// <summary>Higher priority sources get more token budget when space is tight.</summary>
    int Priority { get; }

    /// <summary>
    /// Returns the context content. Called each turn to allow dynamic context.
    /// </summary>
    Task<IReadOnlyList<IContent>> GetContextAsync(CancellationToken ct = default);
}
