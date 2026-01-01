using AgentCore.Chat;
using AgentCore.LLM.Execution;
using AgentCore.LLM.Protocol;
using AgentCore.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace AgentCore.Runtime
{
    public interface IAgentExecutor<T>
    {
        Task<LLMResponse<T>> ExecuteAsync(IAgentContext ctx);
    }
    public sealed class ToolCallingLoop<T> : IAgentExecutor<T>
    {
        private readonly ILogger<ToolCallingLoop<T>> _logger;

        public ToolCallingLoop(ILogger<ToolCallingLoop<T>> logger)
        {
            _logger = logger;
        }

        public async Task<LLMResponse<T>> ExecuteAsync(IAgentContext ctx)
        {
            ctx.ScratchPad.AddUser(ctx.UserRequest ?? "No User input.");

            var llm = ctx.Services.GetRequiredService<ILLMExecutor>();
            var tools = ctx.Services.GetRequiredService<IToolCatalog>();
            var runtime = ctx.Services.GetRequiredService<IToolRuntime>();

            LLMResponse<T>? last = null;

            for (int i = 0; i < ctx.Config.MaxIterations; i++)
            {
                var request = new LLMRequest<T>(
                    prompt: ctx.ScratchPad,
                    toolCallMode: ToolCallMode.Auto,
                    model: ctx.Config.Model,
                    options: ctx.Config.Generation
                )
                {
                    AvailableTools = tools.RegisteredTools
                };

                var result = await llm.ExecuteAsync(
                    request,
                    ctx.CancellationToken,
                    chunk => ctx.Stream?.Invoke(chunk));

                last = result;

                if (!result.HasToolCall)
                    return result;

                var toolResult = await runtime.HandleToolCallAsync(
                    result.ToolCall!,
                    ctx.CancellationToken);

                ctx.ScratchPad.AppendToolCallResult(toolResult);
            }

            if (!ctx.CancellationToken.IsCancellationRequested)
            {
                _logger?.LogWarning(
                    "Agent loop stopped: max iterations reached ({MaxIterations})",
                    ctx.Config.MaxIterations
                );
            }

            return last ?? new LLMResponse<T>
            {
                FinishReason = FinishReason.Cancelled
            };
        }
    }
}
