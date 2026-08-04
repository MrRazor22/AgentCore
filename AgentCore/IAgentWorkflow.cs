using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace AgentCore
{
    public interface IAgentWorkflow
    {
        IAsyncEnumerable<IContent> ExecuteAsync(
            IContext context,
            IContent input,
            JsonSchema? responseSchema,
            CancellationToken ct = default);
    }

    public class ReActWorkflow : IAgentWorkflow
    {
        private readonly ILLM _llm;
        private readonly ITooling _tooling;
        private readonly int? _maxIterations;
        private readonly ILogger<ReActWorkflow>? _logger;

        public ReActWorkflow(
            ILLM llm,
            ITooling tooling,
            int? maxIterations = null,
            ILogger<ReActWorkflow>? logger = null)
        {
            _llm = llm;
            _tooling = tooling;
            _maxIterations = maxIterations;
            _logger = logger;
        }

        public async IAsyncEnumerable<IContent> ExecuteAsync(
            IContext context,
            IContent input,
            JsonSchema? responseSchema,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            int iterations = 0;

            await context.StageAsync(new[] { new Message(Role.User, input) }, ct).ConfigureAwait(false);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (_maxIterations.HasValue && iterations >= _maxIterations.Value)
                {
                    _logger?.LogError("Execution exceeded the maximum limit of {MaxIterations} iterations.", _maxIterations.Value);
                    throw new InvalidOperationException($"Execution exceeded the maximum limit of {_maxIterations.Value} iterations.");
                }

                // Prepare context: handles internal compaction policy, returning the full candidate prompt
                var currentMessages = await context.PreparePromptAsync(ct).ConfigureAwait(false);

                _logger?.LogDebug("Starting execution iteration {Iteration} (Conversation message count: {MessageCount}).", iterations, currentMessages.Count);

                var options = new LLMOptions { ResponseSchema = responseSchema };
                _logger?.LogDebug("Calling LLM StreamAsync...");

                var (assistantMessage, tokenUsage, _) = await _llm
                    .StreamAsync(currentMessages, options, _tooling.Tools, ct)
                    .AccumulateAsync(ct)
                    .ConfigureAwait(false);

                if (assistantMessage == null)
                {
                    _logger?.LogWarning("LLM returned null response.");
                    break;
                }

                // Save LLM response to context immediately (authoritative commit!)
                var finalUsage = tokenUsage ?? new TokenUsage(0, 0);
                await context.CommitAsync(finalUsage, new[] { assistantMessage }, ct).ConfigureAwait(false);

                // Yield all contents produced by LLM assistant response (Text, Reasoning, ToolCall)
                foreach (var content in assistantMessage.Contents)
                {
                    yield return content;
                }

                var toolCalls = assistantMessage.Contents.OfType<ToolCall>().ToList();
                if (toolCalls.Count > 0)
                {
                    iterations++;

                    _logger?.LogDebug("ReActWorkflow: Iteration {Iteration} executing {Count} tool calls.", iterations, toolCalls.Count);
                    var toolResults = await _tooling.ExecuteAsync(toolCalls, ct).ConfigureAwait(false);

                    var toolMessages = new List<Message>();
                    foreach (var result in toolResults)
                    {
                        toolMessages.AddMessage(Role.Tool, result);
                        yield return result;
                    }
                    await context.StageAsync(toolMessages, ct).ConfigureAwait(false);

                    continue;
                }

                break;
            }
        }
    }
}
