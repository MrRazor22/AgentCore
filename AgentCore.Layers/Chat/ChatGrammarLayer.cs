using AgentCore.Context;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Context;

public class ChatGrammarLayer(
    bool coalesceAdjacentRoles = true,
    bool ensureToolPairing = true,
    bool pinSystemInstructions = true,
    bool stripPastReasoning = true) : ContextLayer
{
    public override async Task<IReadOnlyList<Message>> ReadAsync(CancellationToken ct = default)
    {
        var raw = await base.ReadAsync(ct).ConfigureAwait(false);
        return Normalize(raw);
    }

    private IReadOnlyList<Message> Normalize(IReadOnlyList<Message> messages)
    {
        if (messages.Count == 0) return messages;

        var list = messages.ToList();

        if (pinSystemInstructions)
            PinSystem(list);

        if (ensureToolPairing)
            PairTools(list);

        if (stripPastReasoning)
            StripReasoning(list);

        if (coalesceAdjacentRoles)
            list = Coalesce(list);

        return list;
    }

    private static void PinSystem(List<Message> list)
    {
        var sysIdx = list.FindIndex(m => m.Role == Role.System);
        if (sysIdx > 0)
        {
            var sys = list[sysIdx];
            list.RemoveAt(sysIdx);
            list.Insert(0, sys);
        }
    }

    private static void PairTools(List<Message> list)
    {
        var callIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Role == Role.Assistant)
            {
                foreach (var tc in list[i].Contents.OfType<ToolCall>())
                    callIds.Add(tc.Id);
            }
            else if (list[i].Role == Role.Tool)
            {
                var id = list[i].Get<ToolCallId>()?.Value ?? list[i].Id;
                if (string.IsNullOrEmpty(id) || !callIds.Contains(id))
                {
                    list.RemoveAt(i);
                    i--;
                }
            }
        }

        var existingToolIds = list
            .Where(m => m.Role == Role.Tool)
            .Select(m => m.Get<ToolCallId>()?.Value ?? m.Id)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.Ordinal);

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Role == Role.Assistant)
            {
                var calls = list[i].Contents.OfType<ToolCall>().ToList();
                int insertPos = i + 1;
                while (insertPos < list.Count && list[insertPos].Role == Role.Tool)
                    insertPos++;

                foreach (var call in calls)
                {
                    if (!existingToolIds.Contains(call.Id))
                    {
                        list.Insert(insertPos++, new Message(
                            Role.Tool,
                            [new Text($"Tool call '{call.Name}' was aborted.")],
                            call.Id,
                            [new ToolCallId(call.Id)]));
                        existingToolIds.Add(call.Id);
                    }
                }
            }
        }
    }

    private static List<Message> Coalesce(List<Message> list)
    {
        var result = new List<Message>(list.Count);
        foreach (var msg in list)
        {
            if (result.Count > 0 && result[^1].Role == msg.Role && msg.Role is Role.User or Role.Assistant)
            {
                var prev = result[^1];
                var combined = new List<IContent>(prev.Contents);
                foreach (var c in msg.Contents)
                {
                    if (c is Text t && combined.Count > 0 && combined[^1] is Text prevText)
                        combined[^1] = new Text(prevText.Value + "\n" + t.Value);
                    else
                        combined.Add(c);
                }
                result[^1] = new Message(prev.Role, combined, prev.Id, prev.Metadata);
            }
            else
            {
                result.Add(msg);
            }
        }
        return result;
    }

    private static void StripReasoning(List<Message> list)
    {
        int lastUserIdx = list.FindLastIndex(m => m.Role == Role.User);
        for (int i = 0; i < list.Count; i++)
            if (i < lastUserIdx && list[i].Role == Role.Assistant && list[i].Contents.Any(c => c is Reasoning))
                list[i] = new Message(list[i].Role, list[i].Contents.Where(c => c is not Reasoning).ToList(), list[i].Id, list[i].Metadata);
    }
}
