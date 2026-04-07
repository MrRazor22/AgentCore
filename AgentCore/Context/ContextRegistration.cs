namespace AgentCore.Context;

/// <summary>
/// Wraps an IContextSource with its token budget constraint.
/// </summary>
public sealed class ContextRegistration
{
    public IContextSource Source { get; }
    
    /// <summary>Max tokens this source may consume. Null = no limit (flex).</summary>
    public int? MaxTokenBudget { get; }

    public ContextRegistration(IContextSource source, int? maxTokenBudget = null)
    {
        Source = source;
        MaxTokenBudget = maxTokenBudget;
    }
}
