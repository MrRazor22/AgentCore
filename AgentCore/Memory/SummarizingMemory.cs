using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentCore.Memory
{
    public class SummarizingMemory : IMemory
    {
        private readonly IMemory _inner;
        private readonly ILLM _summarizer;
        private readonly ITokenCounter _tokenCounter;
        private readonly LLMCapabilities _capabilities;
        private readonly double _compactionFraction;
        private readonly List<Message> _buffer = new();
        private string _factSheet = string.Empty;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public string FactSheet => _factSheet;

        public SummarizingMemory(
            IMemory inner,
            ILLM summarizer,
            ITokenCounter tokenCounter,
            LLMCapabilities capabilities,
            double compactionFraction = 0.3)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _summarizer = summarizer ?? throw new ArgumentNullException(nameof(summarizer));
            _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            _compactionFraction = compactionFraction;
        }

        public async Task<List<Message>> PrepareAsync(Message newInput, CancellationToken ct = default)
        {
            Message preparedInput = newInput;
            if (!string.IsNullOrEmpty(_factSheet))
            {
                string query = string.Join("\n", newInput.Contents.Select(c => c.ForLlm()));
                var userContents = new List<IContent>
            {
                new Text("<retrieved_context>"),
                new Text(_factSheet),
                new Text("</retrieved_context>"),
                new Text("<query>"),
                new Text(query),
                new Text("</query>")
            };
                preparedInput = new Message(Role.User, userContents);
            }
            return await _inner.PrepareAsync(preparedInput, ct).ConfigureAwait(false);
        }

        public async Task RememberAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
        {
            await _inner.RememberAsync(completedTurn, ct).ConfigureAwait(false);

            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _buffer.AddRange(completedTurn);

                int totalLimit = _capabilities.ContextWindow;
                int reserved = _capabilities.ReservedTokens;
                int safeBudget = totalLimit - reserved;

                int bufferTokens = await _tokenCounter.EstimateAsync(_buffer, ct).ConfigureAwait(false);
                int triggerThreshold = (int)(safeBudget * _compactionFraction);

                if (bufferTokens > triggerThreshold)
                {
                    _factSheet = await ConsolidateAsync(_factSheet, _buffer, ct).ConfigureAwait(false);
                    _buffer.Clear();
                }
            }
            finally
            {
                _lock.Release();
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

            await foreach (var evt in _summarizer.StreamAsync(messages, options: null, tools: null, ct: ct).ConfigureAwait(false))
            {
                if (evt is Text t)
                {
                    sb.Append(t.Value);
                }
            }

            return sb.ToString().Trim();
        }
    }
}
