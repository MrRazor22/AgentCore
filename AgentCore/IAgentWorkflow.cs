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
            StreamingMessage assistantResponse;
            await context.AddAsync([new Message(Role.User, [input])], ct).ConfigureAwait(false);

            do
            {
                ct.ThrowIfCancellationRequested();

                if (_maxIterations.HasValue && iterations >= _maxIterations.Value)
                {
                    _logger?.LogError("Workflow execution exceeded iteration limit. MaxIterations={MaxIterations}", _maxIterations.Value);
                    throw new InvalidOperationException($"Execution exceeded the maximum limit of {_maxIterations.Value} iterations.");
                }

                var chatMessages = await context.GetMessagesAsync(ct).ConfigureAwait(false);

                _logger?.LogInformation("Executing workflow iteration. Iteration={Iteration}, MessageCount={MessageCount}", iterations, chatMessages.Count);

                var msgEvents = _llm.StreamAsync(chatMessages, responseSchema, _tooling.GetDefinitions(), ct);
                assistantResponse = new StreamingMessage(msgEvents);

                await foreach (var content in assistantResponse.ContentsStream(ct).ConfigureAwait(false))
                { 
                    yield return content;
                    if (content is ToolCall toolCall)
                        _ = _tooling.ExecuteAsync(toolCall, ct);
                }

                await context.AddAsync([assistantResponse], ct).ConfigureAwait(false); 

                await foreach (var result in _tooling.StreamResultsAsync(ct).ConfigureAwait(false))
                {
                    await context.AddAsync([new Message(Role.Tool, [result])], ct).ConfigureAwait(false);
                    yield return result;
                }

                iterations++;
            }
            while (assistantResponse.Contents.OfType<ToolCall>().Any());
        }
    }
}
