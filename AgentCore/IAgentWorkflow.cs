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
        private readonly IToolService _tooling;
        private readonly int? _maxIterations;

        public ReActWorkflow(
            ILLM llm,
            IToolService tooling,
            int? maxIterations = null)
        {
            _llm = llm;
            _tooling = tooling;
            _maxIterations = maxIterations;
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
                    yield return new ErrorEvent(new InvalidOperationException($"Execution exceeded the maximum limit of {_maxIterations.Value} iterations."));
                    break;
                }

                var options = new LLMOptions { ResponseSchema = responseSchema };
                var assistantMessage = await _llm
                    .StreamAsync(conversation, options, _tooling.Tools, ct)
                    .AccumulateAsync(ct)
                    .ConfigureAwait(false);

                if (assistantMessage == null)
                {
                    break;
                }

                conversation.Add(assistantMessage);

                var toolCalls = assistantMessage.Contents.OfType<ToolCall>().ToList();
                if (toolCalls.Count > 0)
                {
                    iterations++;

                    foreach (var call in toolCalls)
                    {
                        yield return new ToolCallEvent(call);
                    }

                    var toolMessages = await _tooling.ExecuteAsync(toolCalls, ct).ConfigureAwait(false);
                    conversation.AddRange(toolMessages);

                    foreach (var message in toolMessages)
                    {
                        var result = message.Contents.OfType<ToolResult>().Single();
                        yield return new ToolResultEvent(result);
                    }

                    continue;
                }

                var finalText = string.Join("\n", assistantMessage.Contents.OfType<Text>().Select(t => t.Value)).Trim();
                yield return new AgentResponseEvent<string>(finalText);
                break;
            }
        }
    }
}
