using AgentCore.Conversation;

namespace AgentCore.Context;

/// <summary>
/// A context source backed by a static string. Used for rules, personas, etc.
/// </summary>
public sealed class StaticContextSource : IContextSource
{
    public string Name { get; }
    public Role Role { get; }
    public int Priority { get; }
    
    private readonly IReadOnlyList<IContent> _content;

    public StaticContextSource(string name, string text, Role role = Role.System, int priority = 50)
    {
        Name = name;
        Role = role;
        Priority = priority;
        _content = [new Text(text)];
    }

    public Task<IReadOnlyList<IContent>> GetContextAsync(CancellationToken ct = default)
        => Task.FromResult(_content);
}
