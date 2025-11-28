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
        Conversation Trim(LLMRequestBase req, int? requiredGap = null, string? model = null);
    }

    internal sealed class ContextBudgetManager : IContextBudgetManager
    {
        private readonly ITokenEstimator _estimator;
        private readonly ContextBudgetOptions _opts;

        public ContextBudgetManager(ContextBudgetOptions opts, ITokenEstimator estimator)
        {
            _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));
            _opts = opts ?? throw new ArgumentNullException(nameof(opts));

            if (_opts.MaxContextTokens <= 0)
                _opts.MaxContextTokens = 8000;

            if (_opts.Margin <= 0)
                _opts.Margin = 0.6;
        }

        public Conversation Trim(
            LLMRequestBase req,
            int? requiredGap = null,
            string? model = null)
        {
            if (req == null)
                throw new ArgumentNullException(nameof(req));

            // ---- Determine limits ----
            int limit;
            if (requiredGap != null)
            {
                limit = _opts.MaxContextTokens - requiredGap.Value;
            }
            else
            {
                limit = (int)(_opts.MaxContextTokens * _opts.Margin);
            }
            string tokenizerModel = _opts.TokenizerModel ?? model;

            // We always work on a clone (NON-mutating behavior)
            var trimmed = req.Prompt.Clone();

            int count = _estimator.Estimate(trimmed, model);
            if (count <= limit)
                return trimmed;

            // ---- Keep system messages ----
            var systemMessages = trimmed
                .Where(c => c.Role == Role.System)
                .ToList();

            // ---- Remove tool noise ----
            trimmed.RemoveAll(c => c.Role == Role.Tool);

            count = _estimator.Estimate(trimmed, model);
            if (count <= limit)
                return trimmed;

            // ---- Sliding window for user/assistant context ----
            var core = trimmed
                .Where(c => c.Role == Role.User || c.Role == Role.Assistant)
                .ToList();

            int idx = 0;
            while (count > limit && idx < core.Count - 1) // keep at least 1 turn
            {
                trimmed.Remove(core[idx]);
                idx++;
                count = _estimator.Estimate(trimmed, model);
            }

            // ---- Ensure system messages remain at the top ----
            foreach (var sys in systemMessages)
            {
                if (!trimmed.Contains(sys))
                    trimmed.Insert(0, sys);
            }

            return trimmed;
        }
    }
}
