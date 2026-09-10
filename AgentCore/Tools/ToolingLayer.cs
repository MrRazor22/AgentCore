using AgentCore.LLM.Chat;

namespace AgentCore.Tools;

public abstract class ToolingLayer : ITooling
{
    private bool _attached;

    public ITooling Inner { get; private set; } = null!;

    internal void Attach(ITooling inner)
    {
        if (_attached)
            throw new InvalidOperationException("This tool service decorator has already been attached to a pipeline.");

        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _attached = true;
    }

    public virtual IReadOnlyList<ToolDefinition> GetDefinitions() => Inner.GetDefinitions();

    public virtual async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        await foreach (var result in Inner.ExecuteAsync([call], ct))
            return result;
        throw new InvalidOperationException("No tool result returned.");
    }

    public virtual async IAsyncEnumerable<ToolResult> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (calls is not { Count: > 0 }) yield break;

        var tasks = calls.Select(call => ExecuteAsync(call, ct)).ToList();
        while (tasks.Count > 0)
        {
            var completed = await Task.WhenAny(tasks);
            tasks.Remove(completed);
            yield return await completed;
        }
    }
}
