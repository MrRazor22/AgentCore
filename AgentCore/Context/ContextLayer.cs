using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;

namespace AgentCore.Context;

public abstract class ContextLayer : IContext
{
    private bool _attached;

    /// <summary>
    /// Gets the inner memory layer.
    /// </summary>
    public IContext Inner { get; private set; } = null!;

    internal void Attach(IContext inner)
    {
        if (_attached)
            throw new InvalidOperationException("This memory decorator has already been attached to a pipeline.");

        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _attached = true;
    }

    public virtual IReadOnlyList<Message> Messages => Inner.Messages;

    public virtual Task<List<Message>> PrepareAsync(Message newInput, CancellationToken ct = default)
        => Inner.PrepareAsync(newInput, ct);

    public virtual Task UpdateAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
        => Inner.UpdateAsync(completedTurn, ct);

    public virtual Task ClearAsync(CancellationToken ct = default)
        => Inner.ClearAsync(ct);

    public virtual Task RestoreAsync(IReadOnlyList<Message> history, CancellationToken ct = default)
        => Inner.RestoreAsync(history, ct);
}
