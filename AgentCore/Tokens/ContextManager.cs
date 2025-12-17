using AgentCore.Chat;
using AgentCore.LLM.Protocol;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AgentCore.Tokens
{
    public sealed class ContextBudgetOptions
    {
        public int MaxContextTokens { get; set; } = 8000;
        public double Margin { get; set; } = 0.6;
        public int KeepLastMessages { get; set; } = 4;
    }

    public interface IContextManager
    {
        Conversation Trim(Conversation reqPrompt, int? requiredGap = null);
    }

    public sealed class ContextManager : IContextManager
    {
        private readonly ITokenManager _tokenManager;
        private readonly ContextBudgetOptions _opts;
        private readonly ILogger<ContextManager> _logger;

        public ContextManager(
            ContextBudgetOptions opts,
            ITokenManager tokenManager,
            ILogger<ContextManager> logger)
        {
            _opts = opts ?? throw new ArgumentNullException(nameof(opts));
            _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));
            _logger = logger;
        }
        public Conversation Trim(Conversation reqPrompt, int? requiredGap = null)
        {
            if (reqPrompt == null)
                throw new ArgumentNullException(nameof(reqPrompt));

            // ALWAYS work on a clone
            var source = reqPrompt.Clone();

            int originalCount = _tokenManager.Count(source.ToJson());

            int limit = requiredGap.HasValue
                ? Math.Max(0, _opts.MaxContextTokens - requiredGap.Value)
                : (int)(_opts.MaxContextTokens * _opts.Margin);

            // If already within budget, still return a clone
            if (originalCount <= limit)
                return source;

            // ---- Collect fragments ----
            var system = source.Where(m => m.Role == Role.System).ToList();

            Chat.Chat? lastToolMsg = source.LastOrDefault(m => m.Role == Role.Tool);
            ToolCall? lastToolCall = (lastToolMsg?.Content as ToolCallResult)?.Call;

            var ua = source
                .Where(m => m.Role == Role.User || m.Role == Role.Assistant)
                .Where(m => m.Content is TextContent)
                .ToList();

            var keepUA = ua
                .Skip(Math.Max(0, ua.Count - _opts.KeepLastMessages))
                .ToList();

            // ---- Rebuild once ----
            Conversation Build(IReadOnlyList<Chat.Chat> uaSlice)
            {
                var c = new Conversation();

                foreach (var s in system)
                    c.Add(s.Role, s.Content);

                if (lastToolCall != null)
                    c.AddAssistantToolCall(lastToolCall);

                if (lastToolMsg != null)
                    c.Add(lastToolMsg.Role, lastToolMsg.Content);

                foreach (var m in uaSlice)
                    c.Add(m.Role, m.Content);

                return c;
            }

            var rebuilt = Build(keepUA);
            int finalCount = _tokenManager.Count(rebuilt.ToJson());

            // ---- Shrink UA window ----
            while (finalCount > limit && keepUA.Count > 1)
            {
                keepUA.RemoveAt(0);
                rebuilt = Build(keepUA);
                finalCount = _tokenManager.Count(rebuilt.ToJson());
            }

            _logger.LogDebug(
                "Context trimmed. Original={Original}, Final={Final}, Removed={Removed}",
                originalCount, finalCount, originalCount - finalCount
            );

            return rebuilt;
        }
    }
}
