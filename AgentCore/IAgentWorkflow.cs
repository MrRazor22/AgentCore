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
            Message assistantMessage;
            await context.StageAsync([new Message(Role.User, [input])], ct).ConfigureAwait(false);

            do
            {
                ct.ThrowIfCancellationRequested();

                if (_maxIterations.HasValue && iterations >= _maxIterations.Value)
                {
                    _logger?.LogError("Workflow execution exceeded iteration limit. MaxIterations={MaxIterations}", _maxIterations.Value);
                    throw new InvalidOperationException($"Execution exceeded the maximum limit of {_maxIterations.Value} iterations.");
                }

                var currentMessages = await context.PreparePromptAsync(ct).ConfigureAwait(false);

                _logger?.LogInformation("Executing workflow iteration. Iteration={Iteration}, MessageCount={MessageCount}", iterations, currentMessages.Count);

                var accumulator = new MessageAccumulator();

                await foreach (var content in _llm
                    .StreamAsync(currentMessages, responseSchema, _tooling.GetDefinitions(), ct)
                    .ToContentsAsync(accumulator, ct)
                    .ConfigureAwait(false))
                { 
                    yield return content;
                    if (content is ToolCall toolCall)
                        _ = _tooling.ExecuteAsync(toolCall, ct);
                }

                assistantMessage = accumulator.ToMessage();
                await context.CommitAsync(assistantMessage, ct).ConfigureAwait(false); 

                await foreach (var result in _tooling.StreamResultsAsync(ct).ConfigureAwait(false))
                {
                    await context.StageAsync([new Message(Role.Tool, [result])], ct).ConfigureAwait(false);
                    yield return result;
                }

                iterations++;
            }
            while (assistantMessage.Contents.OfType<ToolCall>().Any());
        }
    }
}
