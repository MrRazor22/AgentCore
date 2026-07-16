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
        private readonly ILLMService _llm;
        private readonly IToolService _tooling;
        private readonly int? _maxIterations;

        public ReActWorkflow(
            ILLMService llm,
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

                var textBuffer = new System.Text.StringBuilder();
                var reasoningBuffer = new System.Text.StringBuilder();
                var toolCalls = new List<ToolCall>();

                var options = new LLMOptions { ResponseSchema = responseSchema };
                await using var enumerator = _llm.StreamAsync(conversation, options, null, ct).GetAsyncEnumerator(ct);

                while (await enumerator.MoveNextAsync())
                {
                    var evt = enumerator.Current;
                    switch (evt)
                    {
                        case Text text:
                            textBuffer.Append(text.Value);
                            break;
                        case Reasoning reasoning:
                            reasoningBuffer.Append(reasoning.Thought);     
                            break;
                        case ToolCall toolCall:
                            toolCalls.Add(toolCall);
                            break;
                    }

                    yield return evt;
                }

                var assistantMessage = BuildMessage(textBuffer, reasoningBuffer, toolCalls);
                if (assistantMessage == null)
                {
                    break;
                }

                conversation.Add(assistantMessage);

                if (toolCalls.Count > 0)
                {
                    iterations++;

                    var toolMessages = await _tooling.ExecuteAsync(toolCalls, ct).ConfigureAwait(false);
                    conversation.AddRange(toolMessages);

                    foreach (var message in toolMessages)
                    {
                        var result = message.Contents.OfType<ToolResult>().Single();
                        yield return new ToolResultEvent(result);
                    }

                    continue;
                }

                // Final assistant response.
                yield return new AgentResponseEvent<string>(textBuffer.ToString().Trim());
                break;
            }
        }

        private static Message? BuildMessage(System.Text.StringBuilder text, System.Text.StringBuilder reasoning, List<ToolCall> toolCalls)
        {
            var contents = new List<IContent>();
            if (reasoning.Length > 0) contents.Add(new Reasoning(reasoning.ToString()));
            
            var textVal = text.ToString().Trim();
            if (!string.IsNullOrEmpty(textVal)) contents.Add(new Text(textVal));
            
            if (toolCalls.Count > 0) contents.AddRange(toolCalls);
            
            return contents.Count == 0 ? null : new Message(Role.Assistant, contents);
        }
    }
}
