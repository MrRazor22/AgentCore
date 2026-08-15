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
        IAsyncEnumerable<IAgentResponse> ExecuteAsync(
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

        public async IAsyncEnumerable<IAgentResponse> ExecuteAsync(
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
                    _logger?.LogError("Workflow execution exceeded iteration limit. MaxIterations={MaxIterations}", _maxIterations.Value);
                    throw new InvalidOperationException($"Execution exceeded the maximum limit of {_maxIterations.Value} iterations.");
                }

                // Prepare context: handles internal compaction policy, returning the full candidate prompt
                var currentMessages = await context.PreparePromptAsync(ct).ConfigureAwait(false);

                _logger?.LogInformation("Executing workflow iteration. Iteration={Iteration}, MessageCount={MessageCount}", iterations, currentMessages.Count);

                var contents = new List<IContent>();
                TokenUsage? tokenUsage = null; 

                try
                {
                    await foreach (var item in _llm
                        .StreamAsync(currentMessages, responseSchema, _tooling.GetDefinitions(), ct)
                        .ConfigureAwait(false))
                    {
                        switch (item)
                        {
                            case IContentDelta delta:
                                delta.AccumulateInto(contents);
                                break;

                            case TokenUsage usage:
                                tokenUsage = usage;
                                yield return usage;
                                break;

                            case IAgentResponse response:
                                yield return response;
                                break;
                        }
                    }

                    foreach (var content in contents)
                    {
                        yield return content;
                    }
                }
                finally
                {
                    if (contents.Count > 0)
                    {
                        var assistantMessage = new Message(Role.Assistant, contents);
                        await context.CommitAsync(new[] { assistantMessage }, tokenUsage, CancellationToken.None).ConfigureAwait(false);
                    }
                }

                ct.ThrowIfCancellationRequested();

                var toolCalls = contents.OfType<ToolCall>().ToList();
                if (toolCalls.Count > 0)
                {
                    iterations++;

                    _logger?.LogInformation("Executing tools. Iteration={Iteration}, ToolCount={ToolCount}", iterations, toolCalls.Count);
                    var toolResults = await _tooling.ExecuteAsync(toolCalls, ct).ConfigureAwait(false);

                    foreach (var result in toolResults) yield return result; 

                    await context.StageAsync(
                        toolResults.Select(r => new Message(Role.Tool, r)).ToList(),
                        ct).ConfigureAwait(false);

                    continue;
                }

                break;
            }
        }
    }
}
