using AgentCore.Chat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;

namespace AgentCore.Runtime;

public interface IAgentContext
{
    AgentConfig Config { get; }
    Conversation ScratchPad { get; }
    string UserInput { get; }
    IServiceProvider Services { get; }
    CancellationToken CancellationToken { get; }
}

public sealed class AgentContext(
    AgentConfig config,
    IServiceProvider services,
    string userInput,
    CancellationToken cancellationToken
) : IAgentContext
{
    public AgentConfig Config => config;
    public IServiceProvider Services => services;
    public string UserInput => userInput;
    public CancellationToken CancellationToken => cancellationToken;
    public Conversation ScratchPad { get; } = new();
}

public interface IAgent
{
    Task<string> InvokeAsync(string input, CancellationToken ct = default);
    IAsyncEnumerable<string> InvokeStreamingAsync(string input, CancellationToken ct = default);
}

public sealed class LLMAgent(IServiceProvider _services, AgentConfig _config) : IAgent
{
    private readonly IAgentMemory _memory = _services.GetRequiredService<IAgentMemory>();
    private readonly ILogger<LLMAgent> _logger = _services.GetRequiredService<ILogger<LLMAgent>>();

    public static AgentBuilder Create(string name = "agent")
        => new AgentBuilder().WithName(name);

    public async Task<string> InvokeAsync(string input, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var chunk in InvokeStreamingAsync(input, ct))
            sb.Append(chunk);
        return sb.ToString();
    }

    public async IAsyncEnumerable<string> InvokeStreamingAsync(
        string input,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var scope = _services.CreateScope();
        var sessionId = Guid.NewGuid().ToString("N");

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["Agent"] = _config.Name,
            ["Session"] = sessionId
        }))
        {
            var ctx = new AgentContext(_config, scope.ServiceProvider, input, ct);
            ctx.ScratchPad.AddSystem(_config.SystemPrompt);

            var executor = scope.ServiceProvider.GetRequiredService<IAgentExecutor>();
            var sb = new StringBuilder();

            await foreach (var chunk in executor.ExecuteStreamingAsync(ctx, ct))
            {
                sb.Append(chunk);
                yield return chunk;
            }

            await _memory.UpdateAsync(sessionId, input, sb.ToString());
        }
    }
}
