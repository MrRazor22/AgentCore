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
        private readonly LLMOptions _options;
        private readonly int _maxIterations;
        private readonly ILogger<ReActExecutor> _logger;

        public ReActExecutor(
            AgentServices services,
            LLMOptions options,
            int maxIterations = 10,
            ILogger<ReActExecutor>? logger = null)
        {
            _services = services;
            _options = options;
            _maxIterations = maxIterations;
            _logger = logger ?? NullLogger<ReActExecutor>.Instance;
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

                if (iterations >= _maxIterations)
                {
                    yield return new TextEvent("You have exceeded the maximum allowed iterations. Stop calling tools and respond to the user immediately.");
                    break;
                }

                Message? assistantMessage = null;
                var textBuffer = new System.Text.StringBuilder();
                var reasoningBuffer = new System.Text.StringBuilder();
                var toolCallsBuffer = new List<ToolCall>();

                var enumerator = _services.Llm.StreamAsync(conversation, _options, responseSchema, ct).GetAsyncEnumerator(ct);
                bool hasContextError = false;
                ContextLengthExceededException? capturedEx = null;
                Exception? otherEx = null;

                try
                {
                    while (true)
                    {
                        LLMEvent evt;
                        try
                        {
                            if (!await enumerator.MoveNextAsync())
                                break;
                            evt = enumerator.Current;
                        }
                        catch (ContextLengthExceededException ex)
                        {
                            hasContextError = true;
                            capturedEx = ex;
                            break;
                        }
                        catch (Exception ex)
                        {
                            otherEx = ex;
                            break;
                        }

                        switch (evt)
                        {
                            case TextEvent te: textBuffer.Append(te.Delta); break;
                            case ReasoningEvent re: reasoningBuffer.Append(re.Delta); break;
                            case ToolCallEvent tc: toolCallsBuffer.Add(tc.Call); break;
                        }

                        yield return evt;
                    }
                }
                finally
                {
                    await enumerator.DisposeAsync();
                }

                // Reconstruct the assistant message from what we buffered
                var contents = new List<IContent>();
                if (reasoningBuffer.Length > 0) contents.Add(new Reasoning(reasoningBuffer.ToString()));
                if (textBuffer.Length > 0) contents.Add(new Text(textBuffer.ToString().Trim()));
                if (toolCallsBuffer.Count > 0) contents.AddRange(toolCallsBuffer);
                if (contents.Count > 0)
                    assistantMessage = new Message(Role.Assistant, contents);

                if (otherEx != null)
                {
                    throw otherEx;
                }

                if (hasContextError && capturedEx != null)
                {
                    yield return new ErrorEvent(capturedEx);
                    break;
                }

                if (assistantMessage == null)
                {
                    break;
                }

                conversation.Add(assistantMessage);

                var toolCalls = assistantMessage.Contents.OfType<ToolCall>().ToList();
                if (toolCalls.Count == 0)
                {
                    break;
                }

                iterations++;

                var toolMessages = await _services.Tooling.ExecuteAsync(toolCalls, ct).ConfigureAwait(false);
                conversation.AddRange(toolMessages);

                foreach (var message in toolMessages)
                {
                    var result = message.Contents.OfType<ToolResult>().Single();
                    yield return new ToolResultEvent(result);
                }
            }
        }
    }
}

