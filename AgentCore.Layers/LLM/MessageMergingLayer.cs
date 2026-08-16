using System.Runtime.CompilerServices;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;

namespace AgentCore.Layers.LLM;

/// <summary>
/// Pipeline layer that normalizes request messages for strict chat template compatibility
/// by merging adjacent text-only User or Assistant messages without mutating context.
/// </summary>
public class MessageMergingLayer : LLMLayer
{ 
    public override async IAsyncEnumerable<ILLMOutput> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    { 
        await foreach (var item in Inner.StreamAsync(MergeTextMessages(messages), responseSchema, tools, ct).WithCancellation(ct).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    public static IReadOnlyList<Message> MergeTextMessages(IReadOnlyList<Message> messages)
    {
        if (messages.Count <= 1) return messages ?? Array.Empty<Message>();

        var result = new List<Message>(messages.Count);

        foreach (var msg in messages)
        {
            if (result.Count == 0)
            {
                result.Add(msg);
                continue;
            }

            var prev = result[^1];

            if (CanMerge(prev, msg))
            {
                var combinedText = GetTextOnlyValue(prev) + "\n" + GetTextOnlyValue(msg);
                result[^1] = new Message(prev.Role, [new Text(combinedText)]);
            }
            else
            {
                result.Add(msg);
            }
        }

        return result;
    }

    private static bool CanMerge(Message prev, Message curr)
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

    private static string GetTextOnlyValue(Message message) => string.Join("\n", message.Contents.OfType<Text>().Select(t => t.Value));
}
