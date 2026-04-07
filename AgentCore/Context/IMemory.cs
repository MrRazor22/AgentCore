using AgentCore.Conversation;

namespace AgentCore.Context;

/// <summary>
/// Agent memory for persisting knowledge across turns/sessions.
/// Automatically registered as an IContextSource.
/// </summary>
public interface IMemory : IContextSource
{
    /// <summary>Store a piece of knowledge the agent wants to remember.</summary>
    Task RememberAsync(string key, string value, CancellationToken ct = default);
    
    /// <summary>Recall a specific piece of knowledge.</summary>
    Task<string?> RecallAsync(string key, CancellationToken ct = default);
    
    /// <summary>Recall all stored knowledge.</summary>
    Task<IReadOnlyDictionary<string, string>> RecallAllAsync(CancellationToken ct = default);
    
    /// <summary>Forget a specific piece of knowledge.</summary>
    Task ForgetAsync(string key, CancellationToken ct = default);
}
