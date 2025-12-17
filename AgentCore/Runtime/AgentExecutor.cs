using AgentCore.Chat;
using AgentCore.LLM.Protocol;
using AgentCore.LLM.Client;
using AgentCore.Tools;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace AgentCore.Runtime
{
    public interface IAgentExecutor
    {
        Task ExecuteAsync(IAgentContext ctx);
    }
    public class ToolCallingLoop : IAgentExecutor
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

        public async Task ExecuteAsync(IAgentContext ctx)
        {
            ctx.ScratchPad.AddUser(ctx.UserRequest ?? "No User input.");

            var llm = ctx.Services.GetRequiredService<ILLMClient>();
            var runtime = ctx.Services.GetRequiredService<IToolRuntime>();

            int iteration = 0;

            while (iteration < _maxIterations && !ctx.CancellationToken.IsCancellationRequested)
            {
                // STREAM LIVE
                var result = await llm.ExecuteAsync<LLMResponse>(
                    new LLMRequest(
                        prompt: ctx.ScratchPad,
                        toolCallMode: _toolMode,
                        model: _model,
                        options: _opts
                    ),
                    ctx.CancellationToken,
                    chunk =>
                    {
                        if (chunk.Kind == StreamKind.Text)
                            ctx.Stream?.Invoke(chunk.AsText());
                    });

                // text 
                ctx.ScratchPad.AddAssistant(result.AssistantMessage);

                // toolcall?
                var toolCall = result.ToolCall; // result.Payload is List<ToolCall>
                if (toolCall == null)
                {
                    ctx.Response.Set(result.AssistantMessage);
                    break;
                }

                // RUN TOOL
                var outputs = await runtime.HandleToolCallAsync(toolCall, ctx.CancellationToken);

                ctx.ScratchPad.AppendToolCallResult(outputs);

                iteration++;
            }
        }
    }
}
