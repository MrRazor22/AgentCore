using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Exceptions;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;

namespace AgentCore
{
    public interface IAgentWorkflow
    {
        IAsyncEnumerable<AgentEvent> ExecuteAsync(
            List<Message> conversation,
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

        public async IAsyncEnumerable<AgentEvent> ExecuteAsync(
            List<Message> conversation,
            JsonSchema? responseSchema,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            int iterations = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (_maxIterations.HasValue && iterations >= _maxIterations.Value)
                {
                    _logger?.LogError("Execution exceeded the maximum limit of {MaxIterations} iterations.", _maxIterations.Value);
                    yield return new ErrorEvent(new InvalidOperationException($"Execution exceeded the maximum limit of {_maxIterations.Value} iterations."));
                    break;
                }

                _logger?.LogDebug("Starting execution iteration {Iteration} (Conversation message count: {MessageCount}).", iterations, conversation.Count);

                var options = new LLMOptions { ResponseSchema = responseSchema };
                _logger?.LogDebug("Calling LLM StreamAsync...");
                var assistantMessage = await _llm
                    .StreamAsync(conversation, options, _tooling.Tools, ct)
                    .AccumulateAsync(ct)
                    .ConfigureAwait(false);

                if (assistantMessage == null)
                {
                    _logger?.LogWarning("LLM returned null response.");
                    break;
                }

                conversation.Add(assistantMessage);

                var texts = assistantMessage.Contents.OfType<Text>().ToList();
                var toolCalls = assistantMessage.Contents.OfType<ToolCall>().ToList();
                var reasonings = assistantMessage.Contents.OfType<Reasoning>().ToList();

                _logger?.LogInformation("LLM response received. Texts: {TextCount}, ToolCalls: {ToolCount}, Reasonings: {ReasoningCount}", texts.Count, toolCalls.Count, reasonings.Count);

                if (toolCalls.Count > 0)
                {
                    iterations++;

                    foreach (var call in toolCalls)
                    {
                        yield return new ToolCallEvent(call);
                    }

                    _logger?.LogDebug("Executing {ToolCount} tool calls...", toolCalls.Count);
                    var toolMessages = await _tooling.ExecuteAsync(toolCalls, ct).ConfigureAwait(false);
                    conversation.AddRange(toolMessages);

                    foreach (var message in toolMessages)
                    {
                        var result = message.Contents.OfType<ToolResult>().Single();
                        yield return new ToolResultEvent(result);
                    }

                    continue;
                }

                var finalText = string.Join("\n", texts.Select(t => t.Value)).Trim();
                yield return new AgentResponseEvent<string>(finalText);
                break;
            }
        }
    }
}
