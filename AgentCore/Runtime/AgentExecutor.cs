using AgentCore.Chat;
using AgentCore.LLM.Execution;
using AgentCore.LLM.Protocol;
using AgentCore.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace AgentCore.Runtime
{
    public sealed class AgentLoopOptions
    {
        public int MaxIterations { get; set; } = 50;
    }
    public interface IAgentExecutor<T>
    {
        Task<LLMResponse<T>> ExecuteAsync(IAgentContext<T> ctx);
    }
    public sealed class ToolCallingLoop<T> : IAgentExecutor<T>
    {
        private readonly AgentLoopOptions _opts;
        private readonly ILogger<ToolCallingLoop<T>> _logger;

        public ToolCallingLoop(
            AgentLoopOptions opts,
            ILogger<ToolCallingLoop<T>> logger)
        {
            _opts = opts;
            _logger = logger;
        }

        public async Task<LLMResponse<T>> ExecuteAsync(IAgentContext<T> ctx)
        {
            ctx.ScratchPad.AddUser(ctx.UserRequest ?? "No User input.");

            var llm = ctx.Services.GetRequiredService<ILLMExecutor>();
            var tools = ctx.Services.GetRequiredService<IToolCatalog>();
            var runtime = ctx.Services.GetRequiredService<IToolRuntime>();

            LLMResponse<T>? last = null;

            for (int i = 0; i < _opts.MaxIterations && !ctx.CancellationToken.IsCancellationRequested; i++)
            {
                var request = ctx.RequestTemplate;
                request.Prompt = ctx.ScratchPad;
                request.AvailableTools ??= tools.RegisteredTools;

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
                    _opts.MaxIterations
                );
            }

            return last ?? new LLMResponse<T>
            {
                FinishReason = FinishReason.Cancelled
            };
        }
    }
}
