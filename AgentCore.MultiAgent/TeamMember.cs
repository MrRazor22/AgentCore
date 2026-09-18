using System.Runtime.CompilerServices;
using AgentCore.LLM.Chat;

namespace AgentCore.MultiAgent;

public interface ITeamMember
{
    string Name { get; }
    IAgent Agent { get; }
    HashSet<string>? Collaborators { get; }
    string Description { get; }
    string Status { get; }
    IAsyncEnumerable<IContentEvent> ExecuteAsync(IEnumerable<IContent> input, CancellationToken ct = default);
}

public sealed class TeamMember(
    string name,
    IAgent agent,
    HashSet<string>? collaborators = null,
    string? description = null) : ITeamMember
{
    private readonly object _sync = new();
    private readonly List<string> _executed = [];
    private string? _runningTool;

    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));
    public IAgent Agent { get; } = agent ?? throw new ArgumentNullException(nameof(agent));
    public HashSet<string>? Collaborators { get; } = collaborators;
    public string Description { get; } = description
        ?? agent.Instructions.OfType<Text>().FirstOrDefault()?.Value
        ?? string.Empty;

    public string Status
    {
        get
        {
            lock (_sync)
            {
                var done = _executed.Count > 0 ? $"executed [{string.Join(", ", _executed.Select(FormatTool))}] " : "";
                _executed.Clear();
                var current = _runningTool != null ? $"and is currently executing '{FormatTool(_runningTool)}'" : "and is synthesizing response";
                return done.Length > 0
                    ? $"{done}{current}"
                    : (_runningTool != null ? $"is currently executing '{FormatTool(_runningTool)}'" : "is working on your request");
            }
        }
    }

    public async IAsyncEnumerable<IContentEvent> ExecuteAsync(
        IEnumerable<IContent> input,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        lock (_sync)
        {
            _executed.Clear();
            _runningTool = null;
        }

        try
        {
            await foreach (var evt in Agent.InvokeStreamingAsync(input as IReadOnlyList<IContent> ?? input.ToArray(), ct: ct).WithCancellation(ct).ConfigureAwait(false))
            {
                if (evt is ToolCallStart tcs)
                {
                    lock (_sync) _runningTool = tcs.Name;
                }
                else if (evt is ToolCall tc)
                {
                    lock (_sync)
                    {
                        _executed.Add(tc.Name);
                        _runningTool = null;
                    }
                }

                yield return evt;
            }
        }
        finally
        {
            lock (_sync)
            {
                _executed.Clear();
                _runningTool = null;
            }
        }
    }

    private static string FormatTool(string toolName) =>
        string.Equals(toolName, "send_agent", StringComparison.OrdinalIgnoreCase) ? "delegating subtask" : toolName;
}
