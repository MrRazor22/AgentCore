using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.LLM.Exceptions;
using AgentCore.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;

namespace AgentCore
{
    public interface IAgentExecutor
    {
        IAsyncEnumerable<AgentEvent> ExecuteAsync(
            List<Message> conversation,
            JsonSchema? responseSchema,
            CancellationToken ct = default);
    }


    public class ReActExecutor : IAgentExecutor
    {
        private readonly AgentServices _services;
        private readonly int? _maxIterations;

        public ReActExecutor(
            AgentServices services,
            int? maxIterations = null)
        {
            _services = services;
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

                await using var enumerator = _services.Llm.StreamAsync(conversation, responseSchema, ct).GetAsyncEnumerator(ct);

                while (await enumerator.MoveNextAsync())
                {
                    var evt = enumerator.Current;
                    switch (evt)
                    {
                        case TextEvent textEvt:
                            textBuffer.Append(textEvt.Delta);
                            break;
                        case ReasoningEvent reasoningEvt:
                            reasoningBuffer.Append(reasoningEvt.Delta);     
                            break;
                        case ToolCallEvent toolCallEvt:
                            toolCalls.Add(toolCallEvt.Call);
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

                    var toolMessages = await _services.Tooling.ExecuteAsync(toolCalls, ct).ConfigureAwait(false);
                    conversation.AddRange(toolMessages);

                    foreach (var message in toolMessages)
                    {
                        var result = message.Contents.OfType<ToolResult>().Single();
                        yield return new ToolResultEvent(result);
                    }

                    continue;
                }

                // Final assistant response.
                yield return new AgentResponseEvent(textBuffer.ToString().Trim());
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

