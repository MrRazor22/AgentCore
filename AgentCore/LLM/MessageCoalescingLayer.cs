using System.Runtime.CompilerServices;
using AgentCore.LLM.Chat;
using AgentCore.Tools;

namespace AgentCore.LLM;

/// <summary>
/// Pipeline layer that normalizes request messages for strict chat template compatibility
/// by coalescing adjacent text-only User or Assistant messages without mutating context.
/// </summary>
public class MessageCoalescingLayer : LLMLayer
{
    public MessageCoalescingLayer(ILLM inner) : base(inner)
    {
    }

    public override async IAsyncEnumerable<ILLMOutput> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var normalizedMessages = CoalesceTextMessages(messages);
        await foreach (var item in Inner.StreamAsync(normalizedMessages, options, tools, ct).WithCancellation(ct).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    public static IReadOnlyList<Message> CoalesceTextMessages(IReadOnlyList<Message> messages)
    {
        if (messages == null || messages.Count <= 1)
            return messages ?? Array.Empty<Message>();

        var result = new List<Message>(messages.Count);

        foreach (var msg in messages)
        {
            if (result.Count == 0)
            {
                result.Add(msg);
                continue;
            }

            var prev = result[^1];

            if (CanCoalesce(prev, msg))
            {
                var combinedText = GetTextOnlyValue(prev) + "\n" + GetTextOnlyValue(msg);
                result[^1] = new Message(prev.Role, new Text(combinedText));
            }
            else
            {
                result.Add(msg);
            }
        }

        return result;
    }

    private static bool CanCoalesce(Message prev, Message curr)
    {
        if (prev.Role != curr.Role) return false;
        if (prev.Role != Role.User && prev.Role != Role.Assistant) return false;

        return IsTextOnly(prev) && IsTextOnly(curr);
    }

    private static bool IsTextOnly(Message message)
    {
        if (message.Contents == null || message.Contents.Count == 0) return false;
        return message.Contents.All(c => c is Text);
    }

    private static string GetTextOnlyValue(Message message)
    {
        return string.Join("\n", message.Contents.OfType<Text>().Select(t => t.Value));
    }
}
