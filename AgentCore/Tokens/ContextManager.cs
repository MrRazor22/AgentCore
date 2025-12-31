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

            // ---- validate requiredGap against margin ----
            if (requiredGap.HasValue)
            {
                if (requiredGap.Value < 0)
                    throw new ArgumentOutOfRangeException(nameof(requiredGap), "Required gap cannot be negative.");

                int maxAllowedGap = (int)(_opts.MaxContextTokens * (1 - _opts.Margin));
                if (requiredGap.Value > maxAllowedGap)
                    throw new ArgumentOutOfRangeException(
                        nameof(requiredGap),
                        $"Required gap {requiredGap.Value} exceeds allowed maximum {maxAllowedGap} tokens based on margin."
                    );
            }

            var source = reqPrompt.Clone();
            int originalCount = _tokenManager.AppromimateCount(source.ToJson());

            int limit = requiredGap.HasValue
                ? Math.Max(0, _opts.MaxContextTokens - requiredGap.Value)
                : (int)(_opts.MaxContextTokens * _opts.Margin);

            var system = source.Where(m => m.Role == Role.System).ToList();

            Chat.Chat? lastToolMsg = source.LastOrDefault(m => m.Role == Role.Tool);
            ToolCall? lastToolCall = (lastToolMsg?.Content as ToolCallResult)?.Call;

            var ua = source
                .Where(m =>
                    (m.Role == Role.User || m.Role == Role.Assistant) &&
                    m.Content is TextContent)
                .ToList();

            // ---- find last complete UA turn ----
            Chat.Chat? lastUser = null;
            Chat.Chat? lastAssistant = null;

            for (int i = ua.Count - 1; i >= 0; i--)
            {
                if (lastUser == null && ua[i].Role == Role.User)
                    lastUser = ua[i];
                else if (lastUser != null && ua[i].Role == Role.Assistant)
                {
                    lastAssistant = ua[i];
                    break;
                }
            }

            var keepUA = ua
                .Skip(Math.Max(0, ua.Count - _opts.KeepLastMessages))
                .ToList();

            if (lastUser != null && !keepUA.Contains(lastUser))
                keepUA.Add(lastUser);

            if (lastAssistant != null && !keepUA.Contains(lastAssistant))
                keepUA.Add(lastAssistant);

            keepUA = keepUA
                .Distinct()
                .OrderBy(m => source.IndexOf(m))
                .ToList();

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

            Conversation result;

            if (originalCount <= limit)
            {
                result = source;
            }
            else
            {
                var rebuilt = Build(keepUA);
                int finalCount = _tokenManager.AppromimateCount(rebuilt.ToJson());

                while (finalCount > limit && keepUA.Count > 0)
                {
                    keepUA.RemoveAt(0);   // drop oldest UA, no exceptions
                    rebuilt = Build(keepUA);
                    finalCount = _tokenManager.AppromimateCount(rebuilt.ToJson());
                }

                result = rebuilt;
            }

            int finalTokens = _tokenManager.AppromimateCount(result.ToJson());
            double usagePct = Math.Round(
                (double)finalTokens / _opts.MaxContextTokens * 100,
                1
            );

            _logger.LogDebug(
                "ContextBudget Original={Original}, Final={Final}, Removed={Removed}, UsagePct={UsagePct}",
                originalCount,
                finalTokens,
                originalCount - finalTokens,
                usagePct
            );

            return result;
        }
    }
}
