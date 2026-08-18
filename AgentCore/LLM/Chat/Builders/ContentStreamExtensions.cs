using System.Runtime.CompilerServices;
using AgentCore.LLM;

namespace AgentCore.LLM.Chat.Builders;

public static class ContentStreamExtensions
{
    /// <summary>
    /// Transforms an incoming stream of <see cref="IContentDelta"/> items into a stream of completed <see cref="IContent"/> items.
    /// </summary>
    public static async IAsyncEnumerable<IContent> BuildContentsAsync(
        this IAsyncEnumerable<IContentDelta> deltas,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var message = new Message(Role.Assistant);

        await foreach (var delta in deltas.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            foreach (var content in message.AddContentDelta(delta))
            {
                yield return content;
            }
        }
    }
}





