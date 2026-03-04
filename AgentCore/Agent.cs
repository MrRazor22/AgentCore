using AgentCore.Chat;
using AgentCore.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;

namespace AgentCore;
 
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
        await foreach (var chunk in CoreInvokeStreamingAsync(input, sessionId, null, ct))
            sb.Append(chunk);
        return sb.ToString();
    }

    public async Task<T?> InvokeAsync<T>(string input, string? sessionId = null, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var chunk in CoreInvokeStreamingAsync(input, sessionId, typeof(T), ct))
            sb.Append(chunk);
            
        var json = sb.ToString();
        if (string.IsNullOrWhiteSpace(json)) return default;
        return System.Text.Json.JsonSerializer.Deserialize<T>(json);
    }

    public IAsyncEnumerable<string> InvokeStreamingAsync(
        string input,
        string? sessionId = null,
        CancellationToken ct = default)
    {
        return CoreInvokeStreamingAsync(input, sessionId, null, ct);
    }

    private async IAsyncEnumerable<string> CoreInvokeStreamingAsync(
        string input,
        string? sessionId,
        Type? outputType,
        [EnumeratorCancellation] CancellationToken ct)
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

            var ctx = new AgentContext(sessionId, _config, scope.ServiceProvider, input, outputType, ct);
            
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
