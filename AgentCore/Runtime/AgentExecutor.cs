using AgentCore.Chat;
using AgentCore.LLM.Execution;
using AgentCore.LLM.Protocol;
using AgentCore.Tools;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace AgentCore.Runtime
{
    public interface IAgentExecutor<T>
    {
        Task<LLMResponse<T>> ExecuteAsync(IAgentContext ctx);
    }

    public sealed class ToolCallingLoop<T> : IAgentExecutor<T>
    {
        private readonly int _maxIterations;
        private readonly LLMGenerationOptions? _opts;

        public ToolCallingLoop(
            ToolCallMode toolMode = ToolCallMode.Auto,
            int maxIterations = 50,
            LLMGenerationOptions? opts = null)
        {
            _maxIterations = maxIterations;
            _opts = opts;
        }

        public async Task<LLMResponse<T>> ExecuteAsync(IAgentContext ctx)
        {
            ctx.ScratchPad.AddUser(ctx.UserRequest ?? "No User input.");

            var llm = ctx.Services.GetRequiredService<ILLMExecutor>();
            var tools = ctx.Services.GetRequiredService<IToolCatalog>();
            var runtime = ctx.Services.GetRequiredService<IToolRuntime>();

            LLMResponse<T>? last = null;

            for (int i = 0; i < _maxIterations && !ctx.CancellationToken.IsCancellationRequested; i++)
            {
                var request = new LLMRequest<T>(
                    prompt: ctx.ScratchPad,
                    options: _opts)
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

            return last ?? new LLMResponse<T>
            {
                FinishReason = FinishReason.Cancelled
            };
        }
    }
}
