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
                    throw new InvalidOperationException($"Execution exceeded the maximum limit of {_maxIterations.Value} iterations.");

                var chatMessages = await context.GetMessagesAsync(ct).ConfigureAwait(false);

                _logger?.LogInformation("Executing workflow iteration. Iteration={Iteration}, MessageCount={MessageCount}", iterations, chatMessages.Count);

                var msgEvents = _llm.StreamAsync(chatMessages, responseSchema, _tooling.GetDefinitions(), ct);
                assistantResponse = new (Role.Assistant);
                var toolExecutionTasks = new List<Task<ToolResult>>();

                await foreach (var content in assistantResponse.Receive(msgEvents, ct).ConfigureAwait(false))
                { 
                    yield return content;
                    if (content is ToolCall toolCall)
                        toolExecutionTasks.Add(_tooling.ExecuteAsync(toolCall, ct));
                }

                await context.AddAsync([assistantResponse], ct).ConfigureAwait(false); 

                while (toolExecutionTasks.Count > 0)
                {
                    var completedTask = await Task.WhenAny(toolExecutionTasks).ConfigureAwait(false);
                    toolExecutionTasks.Remove(completedTask);
                    var result = await completedTask.ConfigureAwait(false);

                    await context.AddAsync([new Message(Role.Tool, [result])], ct).ConfigureAwait(false);
                    yield return result;
                }

                iterations++;
            }
            while (assistantResponse.Contents.OfType<ToolCall>().Any());
        }
    }
}
