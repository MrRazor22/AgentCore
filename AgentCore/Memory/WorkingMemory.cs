using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentCore.Memory
{
    public class WorkingMemory : IMemory
    {
        private readonly List<Message> _history = new();
        private readonly ITokenCounter _tokenCounter;
        private readonly LLMCapabilities _capabilities;
        private readonly IReadOnlyList<Tool> _tools;
        private readonly IContent? _instructions;
        private readonly double _retentionTarget;
        private readonly ILLM? _summarizer;
        private string _factSheet = string.Empty;

        public WorkingMemory(
            ITokenCounter tokenCounter,
            LLMCapabilities capabilities,
            IReadOnlyList<Tool> tools,
            IContent? instructions,
            double retentionTarget = 0.70,
            ILLM? summarizer = null)
        {
            _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            _tools = tools ?? throw new ArgumentNullException(nameof(tools));
            _instructions = instructions;
            _retentionTarget = retentionTarget;
            _summarizer = summarizer;
        }

        public async Task<List<Message>> PrepareAsync(Message newInput, CancellationToken ct = default)
        {
            // 1. Measure tokens of fixed parts of the prompt
            var fixedMessages = new List<Message>();
            if (_instructions != null) fixedMessages.Add(new Message(Role.System, _instructions));

            Message? summaryMessage = null;
            if (_summarizer != null && !string.IsNullOrEmpty(_factSheet))
            {
                summaryMessage = new Message(Role.User, new Text("Another language model started to solve this problem and produced a summary of its thinking process. Use this to build on the work that has already been done and avoid duplicating work. Here is the summary produced by the other language model, use the information in this summary to assist with your own analysis:\n\n" + _factSheet));
                fixedMessages.Add(summaryMessage);
            }
            fixedMessages.Add(newInput);

            // 2. Compute remaining budget for rolling history
            int totalLimit = _capabilities.ContextWindow;
            int fixedTokens = await _tokenCounter.EstimateAsync(fixedMessages, ct).ConfigureAwait(false);
            int toolTokens = _tools.Count > 0 ? await _tokenCounter.EstimateAsync(_tools, ct).ConfigureAwait(false) : 0;
            int budget = Math.Max(0, totalLimit - (fixedTokens + toolTokens + _capabilities.ReservedTokens));

            List<Message> workingHistory;
            lock (_history)
            {
                workingHistory = _history.ToList();
            }

            int historyTokens = await _tokenCounter.EstimateAsync(workingHistory, ct).ConfigureAwait(false);

            // Prune the local copy to fit the exact final budget (no mutation to master history)
            if (historyTokens > budget)
            {
                int targetLimit = (int)(budget * _retentionTarget);
                while (historyTokens > targetLimit && workingHistory.Count > 0)
                {
                    workingHistory.RemoveAt(0); // prune oldest
                    historyTokens = await _tokenCounter.EstimateAsync(workingHistory, ct).ConfigureAwait(false);
                }
            }

            // 3. Construct conversation
            var conversation = new List<Message>();
            if (_instructions != null) conversation.Add(new Message(Role.System, _instructions));
            if (summaryMessage != null) conversation.Add(summaryMessage);
            conversation.AddRange(workingHistory);
            conversation.Add(newInput);

            return conversation;
        }

        public async Task RememberAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
        {
            if (completedTurn == null || completedTurn.Count == 0) return;

            List<Message> workingHistory;
            lock (_history)
            {
                _history.AddRange(completedTurn);
                workingHistory = _history.ToList();
            }

            // Calculate budget for history
            var fixedMessages = new List<Message>();
            if (_instructions != null) fixedMessages.Add(new Message(Role.System, _instructions));
            if (_summarizer != null && !string.IsNullOrEmpty(_factSheet))
            {
                fixedMessages.Add(new Message(Role.User, new Text(_factSheet)));
            }

            int fixedTokens = await _tokenCounter.EstimateAsync(fixedMessages, ct).ConfigureAwait(false);
            int toolTokens = _tools.Count > 0 ? await _tokenCounter.EstimateAsync(_tools, ct).ConfigureAwait(false) : 0;
            int budget = Math.Max(0, _capabilities.ContextWindow - (fixedTokens + toolTokens + _capabilities.ReservedTokens));

            int historyTokens = await _tokenCounter.EstimateAsync(workingHistory, ct).ConfigureAwait(false);

            if (historyTokens > budget)
            {
                int targetLimit = (int)(budget * _retentionTarget);
                var evicted = new List<Message>();

                while (historyTokens > targetLimit && workingHistory.Count > 0)
                {
                    evicted.Add(workingHistory[0]);
                    workingHistory.RemoveAt(0); // prune oldest
                    historyTokens = await _tokenCounter.EstimateAsync(workingHistory, ct).ConfigureAwait(false);
                }

                string? newFactSheet = null;
                if (_summarizer != null && evicted.Count > 0)
                {
                    newFactSheet = await ConsolidateAsync(_factSheet, evicted, ct).ConfigureAwait(false);
                }

                lock (_history)
                {
                    _history.Clear();
                    _history.AddRange(workingHistory);
                    if (newFactSheet != null)
                    {
                        _factSheet = newFactSheet;
                    }
                }
            }
        }

        private async Task<string> ConsolidateAsync(string existingFactSheet, List<Message> turns, CancellationToken ct)
        {
            var sbTurns = new StringBuilder();
            foreach (var turn in turns)
            {
                sbTurns.AppendLine($"{turn.Role}: {string.Join("\n", turn.Contents.Select(c => c.ForLlm()))}");
            }

            var prompt = new Message(Role.System, new Text(
                "You are a memory consolidation assistant. Your task is to update the existing distilled fact sheet with new conversation turns. Add new facts, preference profiles, and user details, resolve any logical conflicts, and remove outdated instructions. Do not lose critical context. Keep the fact sheet concise, bulleted, and structured. Do not output conversational responses or logs; only output the updated fact sheet."));

            var userContext = new Message(Role.User, new Text(
                $"Existing Fact Sheet:\n{existingFactSheet}\n\nNew Conversation Turns:\n{sbTurns}"));

            var messages = new List<Message> { prompt, userContext };
            var sb = new StringBuilder();

            await foreach (var evt in _summarizer!.StreamAsync(messages, options: null, tools: null, ct: ct).ConfigureAwait(false))
            {
                if (evt is Text t)
                {
                    sb.Append(t.Value);
                }
            }

            return sb.ToString().Trim();
        }

        public Task ClearAsync(CancellationToken ct = default)
        {
            lock (_history)
            {
                _history.Clear();
                _factSheet = string.Empty;
            }
            return Task.CompletedTask;
        }

        public Task RestoreAsync(IReadOnlyList<Message> history, CancellationToken ct = default)
        {
            lock (_history)
            {
                _history.Clear();
                _factSheet = string.Empty;
                if (history != null && history.Count > 0)
                {
                    _history.AddRange(history);
                }
            }
            return Task.CompletedTask;
        }
    }

}
