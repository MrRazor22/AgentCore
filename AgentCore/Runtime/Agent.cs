using AgentCore.Chat;
using AgentCore.Tooling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;

namespace AgentCore.Runtime;

public interface IAgentContext
{
    string SessionId { get; }
    AgentConfig Config { get; }
    IList<Message> Messages { get; }
    string UserInput { get; }
    IServiceProvider Services { get; }
    CancellationToken CancellationToken { get; }
}

public sealed class AgentContext(
    string sessionId,
    AgentConfig config,
    IServiceProvider services,
    string userInput,
    CancellationToken cancellationToken
) : IAgentContext
{
    public string SessionId => sessionId;
    public AgentConfig Config => config;
    public IServiceProvider Services => services;
    public string UserInput => userInput;
    public CancellationToken CancellationToken => cancellationToken;
    public IList<Message> Messages { get; } = new List<Message>();
}

public interface IAgent
{
    Task<string> InvokeAsync(string input, string? sessionId = null, CancellationToken ct = default);
    Task<T?> InvokeAsync<T>(string input, string? sessionId = null, CancellationToken ct = default);
    IAsyncEnumerable<string> InvokeStreamingAsync(string input, string? sessionId = null, CancellationToken ct = default);
}

public sealed class LLMAgent(IServiceProvider _services, AgentConfig _config) : IAgent
{
    private readonly IAgentMemory _memory = _services.GetRequiredService<IAgentMemory>();
    private readonly ILogger<LLMAgent> _logger = _services.GetRequiredService<ILogger<LLMAgent>>();

    public static AgentBuilder Create(string name = "agent")
        => new AgentBuilder().WithName(name);

    public async Task<string> InvokeAsync(string input, string? sessionId = null, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var chunk in InvokeStreamingAsync(input, sessionId, ct))
            sb.Append(chunk);
        return sb.ToString();
    }

    public async Task<T?> InvokeAsync<T>(string input, string? sessionId = null, CancellationToken ct = default)
    {
        var originalOutput = _config.OutputType;
        _config.OutputType = typeof(T);
        try
        {
            var json = await InvokeAsync(input, sessionId, ct);
            if (string.IsNullOrWhiteSpace(json)) return default;
            return System.Text.Json.JsonSerializer.Deserialize<T>(json);
        }
        finally
        {
            _config.OutputType = originalOutput;
        }
    }

    public async IAsyncEnumerable<string> InvokeStreamingAsync(
        string input,
        string? sessionId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var scope = _services.CreateScope();
        sessionId ??= Guid.NewGuid().ToString("N");

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["Agent"] = _config.Name,
            ["Session"] = sessionId
        }))
        {
            // Tools are already registered in the scoped ToolRegistry via AgentBuilder

            var ctx = new AgentContext(sessionId, _config, scope.ServiceProvider, input, ct);
            
            var pastMessages = await _memory.RecallAsync(sessionId);
            if (pastMessages.Count > 0)
            {
                // Resume from existing state
                foreach (var msg in pastMessages)
                {
                    ctx.Messages.Add(msg);
                }
            }
            else
            {
                // New session
                ctx.Messages.AddSystem(_config.SystemPrompt);
            }

            var executor = scope.ServiceProvider.GetRequiredService<IAgentExecutor>();

            await foreach (var chunk in executor.ExecuteStreamingAsync(ctx, ct))
            {
                yield return chunk;
            }
        }
    }
}
