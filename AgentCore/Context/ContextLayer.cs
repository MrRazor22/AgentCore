using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;

namespace AgentCore.Context;

public abstract class ContextLayer : IContext, IMemoryFinalizer
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

    public virtual Task AddAsync(Message message, CancellationToken ct = default)
        => Inner.AddAsync(message, ct);

    public virtual Task AddRangeAsync(IEnumerable<Message> messages, CancellationToken ct = default)
        => Inner.AddRangeAsync(messages, ct);

    public virtual Task ClearAsync(CancellationToken ct = default)
        => Inner.ClearAsync(ct);

    public virtual async Task FinalizeTurnAsync(CancellationToken ct = default)
    {
        if (Inner is IMemoryFinalizer finalizer)
        {
            await finalizer.FinalizeTurnAsync(ct).ConfigureAwait(false);
        }
    }
}

