using AgentCore.Chat;
using AgentCore.LLM.Client;
using System;
using System.Linq;

namespace AgentCore.Tokens
{
    public sealed class ContextBudgetOptions
    {
        public int MaxContextTokens { get; set; } = 8000;
        public double Margin { get; set; } = 0.6;
        public string? TokenizerModel { get; set; } = null;
    }

    public interface IContextBudgetManager
    {
        Conversation Trim(LLMRequestBase req, int? requiredGap = null);
    }

    internal sealed class ContextBudgetManager : IContextBudgetManager
    {
        private readonly ITokenManager _tokenManager;
        private readonly ContextBudgetOptions _opts;

        public ContextBudgetManager(ContextBudgetOptions opts, ITokenManager tokenManager)
        {
            _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));
            _opts = opts ?? throw new ArgumentNullException(nameof(opts));

            if (_opts.MaxContextTokens <= 0)
                _opts.MaxContextTokens = 8000;

            if (_opts.Margin <= 0)
                _opts.Margin = 0.6;
        }

        public Conversation Trim(
            LLMRequestBase req,
            int? requiredGap = null)
        {
            if (req == null)
                throw new ArgumentNullException(nameof(req));

            int limit;
            if (requiredGap != null)
                limit = _opts.MaxContextTokens - requiredGap.Value;
            else
                limit = (int)(_opts.MaxContextTokens * _opts.Margin);

            // clone always
            var trimmed = req.Prompt.Clone();

            int count = _tokenManager.Count(trimmed.ToJson());
            if (count <= limit)
                return trimmed;

            // keep system
            var systemMessages = trimmed
                .Where(c => c.Role == Role.System)
                .ToList();

            // drop tool messages
            trimmed.RemoveAll(c => c.Role == Role.Tool);

            count = _tokenManager.Count(trimmed.ToJson());
            if (count <= limit)
                return trimmed;

            // sliding window
            var core = trimmed
                .Where(c => c.Role == Role.User || c.Role == Role.Assistant)
                .ToList();

            int idx = 0;
            while (count > limit && idx < core.Count - 1)
            {
                trimmed.Remove(core[idx]);
                idx++;
                count = _tokenManager.Count(trimmed.ToJson());
            }

            // ensure system on top
            foreach (var sys in systemMessages)
            {
                if (!trimmed.Contains(sys))
                    trimmed.Insert(0, sys);
            }

            return trimmed;
        }
    }

}
