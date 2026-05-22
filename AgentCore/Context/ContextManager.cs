using AgentCore.Conversation;
using AgentCore.Tokens;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AgentCore.Context;

/// <summary>
/// Implementation of context management that handles system message preservation,
/// rolling window context reduction, and large output truncation.
/// </summary>
public sealed class ContextManager : IContextManager
{
    private readonly ITokenCounter _tokenCounter;
    private readonly ILogger<ContextManager> _logger;
    private const int MaxToolResultChars = 8000;

    public ContextManager(ITokenCounter tokenCounter, ILogger<ContextManager>? logger = null)
    {
        _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
        _logger = logger ?? NullLogger<ContextManager>.Instance;
    }

    public IReadOnlyList<Message> Manage(IReadOnlyList<Message> state, int tokenBudget)
    {
        _logger.LogDebug("Context fitting: TargetBudget={Budget} InputMessagesCount={Count}", tokenBudget, state.Count);

        var result = new List<Message>();

        // 1. Preserve all system messages at the top
        var systemMessages = state.Where(m => m.Role == Role.System).ToList();
        result.AddRange(systemMessages);

        // Approximate token cost of system messages
        int systemTokens = systemMessages.Sum(m => ApproxCount(GetContentString(m)));
        tokenBudget -= systemTokens;

        // 2. Add non-system messages from newest to oldest until budget is exhausted
        var nonSystem = state.Where(m => m.Role != Role.System).ToList();
        var reversed = nonSystem.AsEnumerable().Reverse().ToList();
        var added = new List<Message>();

        foreach (var msg in reversed)
        {
            var content = GetContentString(msg);
            var tokens = ApproxCount(content);

            if (tokens > tokenBudget)
            {
                // Truncate this message if it's the last one we can fit, preserving context flow
                var truncated = content.Length > MaxToolResultChars
                    ? content[..MaxToolResultChars] + "\n[Output truncated...]"
                    : content;

                _logger.LogWarning("Context limit reached: Message truncated to fit budget. Role={Role} OriginalLength={Len} TruncatedLength={TruncatedLen}", 
                    msg.Role, content.Length, truncated.Length);

                added.Add(new Message(msg.Role, new Text(truncated)));
                break;
            }

            added.Add(msg);
            tokenBudget -= tokens;
            if (tokenBudget <= 0) break;
        }

        int skippedCount = reversed.Count - added.Count;
        if (skippedCount > 0)
        {
            _logger.LogInformation("Context reduction triggered: Dropped {DroppedCount} oldest messages from sliding window to fit budget.", skippedCount);
        }

        // Chronologically restore the order of the conversation history
        added.Reverse();
        result.AddRange(added);

        return result;
    }

    private static string GetContentString(Message msg)
        => string.Join("", msg.Contents.Select(c => c.ForLlm()));

    private static int ApproxCount(string text)
        => (int)Math.Ceiling(text.Length / 4.0);
}
