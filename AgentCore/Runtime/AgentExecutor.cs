using AgentCore.Chat;
using AgentCore.LLM.Protocol;
using AgentCore.LLM.Execution;
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
        private readonly string? _model;
        private readonly ToolCallMode _toolMode;
        private readonly int _maxIterations;
        private readonly LLMGenerationOptions? _opts;

        public ToolCallingLoop(
            string? model = null,
            ToolCallMode toolMode = ToolCallMode.Auto,
            int maxIterations = 50,
            LLMGenerationOptions? opts = null)
        {
            _model = model;
            _toolMode = toolMode;
            _maxIterations = maxIterations;
            _opts = opts;
        }

        public async Task<LLMResponse<T>> ExecuteAsync(IAgentContext ctx)
        {
            ctx.ScratchPad.AddUser(ctx.UserRequest ?? "No User input.");

            var llm = ctx.Services.GetRequiredService<ILLMExecutor>();
            var runtime = ctx.Services.GetRequiredService<IToolRuntime>();

            int iteration = 0;
            LLMResponse<T>? last = null;

            while (iteration < _maxIterations && !ctx.CancellationToken.IsCancellationRequested)
            {
                var result = await llm.ExecuteAsync(
                    new LLMRequest<T>(
                        prompt: ctx.ScratchPad,
                        toolCallMode: _toolMode,
                        model: _model,
                        options: _opts)
                    {
                        AvailableTools = ctx.Services.GetRequiredService<IToolCatalog>().RegisteredTools
                    },
                    ctx.CancellationToken,
                    chunk =>
                    {
                        if (typeof(T) == typeof(string) && chunk.Kind == StreamKind.Text)
                            ctx.Stream?.Invoke(chunk.AsText());
                    });

                last = result;

                // No tool → final answer
                if (!result.HasToolCall)
                    return result;

                // Run tool
                var outputs = await runtime.HandleToolCallAsync(
                    result.ToolCall!,
                    ctx.CancellationToken);

                ctx.ScratchPad.AppendToolCallResult(outputs);
                iteration++;
            }

            return last ?? new LLMResponse<T>
            {
                FinishReason = FinishReason.Cancelled
            };
        }
    }

}
