using AgentCore.Chat;
using AgentCore.LLM.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

namespace AgentCore.Tokens
{
    public sealed class ContextBudgetOptions
    {
        public int MaxContextTokens { get; set; } = 8000;
        public double Margin { get; set; } = 0.6;
        public int KeepLastMessages { get; set; } = 4;
    }

    public interface IContextBudgetManager
    {
        Conversation Trim(Conversation reqPrompt, int? requiredGap = null);
    }

    public sealed class ContextBudgetManager : IContextBudgetManager
    {
        private readonly ITokenManager _tokenManager;
        private readonly ContextBudgetOptions _opts;
        private readonly ILogger<ContextBudgetManager> _logger;

        public ContextBudgetManager(
            ContextBudgetOptions opts,
            ITokenManager tokenManager,
            ILogger<ContextBudgetManager> logger)
        {
            _opts = opts ?? throw new ArgumentNullException(nameof(opts));
            _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));
            _logger = logger;
        }
        public Conversation Trim(Conversation reqPrompt, int? requiredGap = null)
        {
            if (reqPrompt == null)
                throw new ArgumentNullException(nameof(reqPrompt));

            int keepLast = _opts.KeepLastMessages;
            int originalCount = _tokenManager.Count(reqPrompt.ToJson());

            int limit = requiredGap != null
                ? _opts.MaxContextTokens - requiredGap.Value
                : (int)(_opts.MaxContextTokens * _opts.Margin);

            if (originalCount <= limit)
                return reqPrompt;

            var clone = reqPrompt.Clone();

            // ---- 1. Collect fragments ----
            var system = clone.Where(m => m.Role == Role.System).ToList();

            // Last tool result and its call
            Chat.Chat? lastToolMsg = clone.LastOrDefault(m => m.Role == Role.Tool);
            ToolCall? lastToolCall = (lastToolMsg?.Content as ToolCallResult)?.Call;

            // Remove all tool messages from UA window
            var ua = clone
                .Where(m => m.Role == Role.User || m.Role == Role.Assistant)
                .Where(m => m.Content is TextContent) // <-- important: avoid keeping assistant tool-call
                .ToList();

            var keepUA = ua.Skip(Math.Max(0, ua.Count - keepLast)).ToList();

            // ---- 2. Rebuild ----
            var rebuilt = new Conversation();

            foreach (var s in system)
                rebuilt.Add(s.Role, s.Content);

            if (lastToolCall != null)
                rebuilt.AddAssistantToolCall(lastToolCall);

            if (lastToolMsg != null)
                rebuilt.Add(lastToolMsg.Role, lastToolMsg.Content);

            foreach (var msg in keepUA)
                rebuilt.Add(msg.Role, msg.Content);

            int finalCount = _tokenManager.Count(rebuilt.ToJson());

            // ---- 3. If too large, shrink UA window ----
            while (finalCount > limit && keepUA.Count > 1)
            {
                keepUA.RemoveAt(0);

                rebuilt = new Conversation();

                foreach (var s in system)
                    rebuilt.Add(s.Role, s.Content);

                if (lastToolCall != null)
                    rebuilt.AddAssistantToolCall(lastToolCall);

                if (lastToolMsg != null)
                    rebuilt.Add(lastToolMsg.Role, lastToolMsg.Content);

                foreach (var msg in keepUA)
                    rebuilt.Add(msg.Role, msg.Content);

                finalCount = _tokenManager.Count(rebuilt.ToJson());
            }

            _logger.LogWarning(
                "Context trimmed. Original={Original}, Final={Final}, Removed={Removed}",
                originalCount, finalCount, originalCount - finalCount
            );

            return rebuilt;
        }
    }
}
