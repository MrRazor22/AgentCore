using AgentCore.Conversation;
using AgentCore.Tokens;

namespace AgentCore.Memory;

public sealed class DefaultContextAssembler : IContextAssembler
{
    private readonly ITokenCounter _tokenCounter;
    private const int MaxToolResultChars = 8000;

    public DefaultContextAssembler(ITokenCounter tokenCounter)
    {
        _tokenCounter = tokenCounter;
    }

    public IReadOnlyList<Message> Assemble(
        IReadOnlyList<Message> memory,
        IReadOnlyList<Message> conversation,
        int tokenBudget)
    {
        var result = new List<Message>();

        // 1. Always keep system messages first
        var systemMessages = memory.Where(m => m.Role == Role.System).ToList();
        result.AddRange(systemMessages);

        // 2. Add conversation from newest to oldest until budget
        var reversed = conversation.Reverse().ToList();
        
        foreach (var msg in reversed)
        {
            var content = GetContentString(msg);
            var tokens = ApproxCount(content);

            if (tokens > tokenBudget)
            {
                // Truncate this message
                var truncated = content.Length > MaxToolResultChars
                    ? content[..MaxToolResultChars] + "\n[Output truncated...]"
                    : content;
                result.Add(new Message(msg.Role, new Text(truncated)));
                break;
            }

            result.Add(msg);
            tokenBudget -= tokens;
            if (tokenBudget <= 0) break;
        }

        // Reverse to get back to chronological order
        result.Reverse();
        return result;
    }

    private static string GetContentString(Message msg)
        => string.Join("", msg.Contents.Select(c => c.ForLlm()));

    private int ApproxCount(string text)
        => (int)Math.Ceiling(text.Length / 4.0);
}