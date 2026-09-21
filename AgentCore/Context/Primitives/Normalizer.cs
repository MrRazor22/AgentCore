using System;
using System.Collections.Generic;
using System.Linq;
using AgentCore.LLM.Chat;

namespace AgentCore.Context.Primitives;

public interface INormalizer
{
    IReadOnlyList<Message> Normalize(IReadOnlyList<Message> messages);
}

public class ChatNormalizer(
    bool ensureToolPairing = true,
    bool coalesceAdjacentRoles = true,
    bool stripPastReasoning = true) : INormalizer
{
    public IReadOnlyList<Message> Normalize(IReadOnlyList<Message> messages)
    {
        if (messages.Count == 0) return messages;

        var list = messages.ToList();
        if (ensureToolPairing) list = PairTools(list);
        if (stripPastReasoning) list = StripReasoning(list);
        if (coalesceAdjacentRoles) list = Coalesce(list);

        return list;
    }

    private static List<Message> PairTools(List<Message> list)
    {
        var toolIds = list.Where(m => m.Role == Role.Assistant)
            .SelectMany(m => m.Contents.OfType<ToolCall>().Select(c => c.Id)).ToHashSet(StringComparer.Ordinal);
        var doneIds = list.Where(m => m.Role == Role.Tool)
            .SelectMany(GetToolCallIds).Where(id => !string.IsNullOrEmpty(id)).ToHashSet(StringComparer.Ordinal);

        var result = new List<Message>(list.Count);
        foreach (var msg in list)
        {
            if (msg.Role == Role.Tool)
            {
                var ids = GetToolCallIds(msg).ToList();
                if (ids.Count > 0 && !ids.Any(toolIds.Contains)) continue;
                if (ids.Count == 0 && (string.IsNullOrEmpty(msg.Id) || !toolIds.Contains(msg.Id))) continue;
            }

            result.Add(msg);

            if (msg.Role == Role.Assistant)
            {
                foreach (var call in msg.Contents.OfType<ToolCall>().Where(c => !doneIds.Contains(c.Id)))
                {
                    result.Add(new Message(Role.Tool, [new ToolResult(call.Id, [new Text($"Tool call '{call.Name}' was aborted.")], isError: true)], call.Id));
                    doneIds.Add(call.Id);
                }
            }
        }
        return result;

        static IEnumerable<string> GetToolCallIds(Message m)
        {
            var fromResults = m.Contents.OfType<ToolResult>().Select(tr => tr.ToolCallId);
            return fromResults.Any() ? fromResults : (m.Id != null ? [m.Id] : []);
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

    private static List<Message> StripReasoning(List<Message> list)
    {
        int lastUserIdx = list.FindLastIndex(m => m.Role == Role.User);
        for (int i = 0; i < list.Count; i++)
            if (i < lastUserIdx && list[i].Role == Role.Assistant && list[i].Contents.Any(c => c is Reasoning))
                list[i] = new Message(list[i].Role, list[i].Contents.Where(c => c is not Reasoning).ToList(), list[i].Id, list[i].Metadata);
        return list;
    }
}
