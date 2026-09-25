using AgentCore.Context.Primitives;
using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;

namespace AgentCore.Context;

public static class ContextExtensions
{
    public static async Task WriteAsync(this IContext context, IMessageEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(evt);
        await foreach (var _ in context.WriteAsync(Stream(evt), ct).ConfigureAwait(false)) { }
        static async IAsyncEnumerable<IMessageEvent> Stream(IMessageEvent e) { yield return e; }
    }

    public static TTarget? Find<TTarget>(this IContext? context, Action<TTarget>? configure = null) where TTarget : class
    {
        for (var curr = context; curr != null; curr = curr is ILayer<IContext> l ? l.Inner : null)
            if (curr is TTarget match) { configure?.Invoke(match); return match; }
        return null;
    }

    public static IContext Configure(
        this IContext? context,
        int contextWindow = 50000, int? reserveTokens = null, int? maxSingleMessageTokens = null,
        ICompactor? compactor = null, ITokenizer? counter = null, ITruncator? truncator = null,
        IAssembler? assembler = null, INormalizer? normalizer = null, ILogger<ChatContext>? logger = null,
        IEnumerable<Message>? messages = null)
            => new ChatContext(contextWindow, reserveTokens, maxSingleMessageTokens, compactor, counter, truncator, assembler, normalizer, logger, messages);

    public static IReadOnlyList<Message> Snapshot(this IReadOnlyList<Message> messages, string? upToMessageId = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (upToMessageId == null) return messages;
        for (int i = 0; i < messages.Count; i++)
            if (string.Equals(messages[i].Id, upToMessageId, StringComparison.Ordinal))
                return messages.Take(i + 1).ToList();
        return messages;
    }
}
