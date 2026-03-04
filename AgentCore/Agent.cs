using AgentCore.Chat;
using AgentCore.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;

namespace AgentCore;
 
public interface IAgent
{
    Task<string> InvokeAsync(string input, string? sessionId = null, CancellationToken ct = default);
    Task<T?> InvokeAsync<T>(string input, string? sessionId = null, CancellationToken ct = default);
    IAsyncEnumerable<string> InvokeStreamingAsync(string input, string? sessionId = null, CancellationToken ct = default);
}

public sealed class LLMAgent : IAgent
{
    private readonly IAgentExecutor _executor;
    private readonly IAgentMemory _memory;
    private readonly AgentConfig _config;
    private readonly ILogger<LLMAgent> _logger;

    public LLMAgent(
        IAgentExecutor executor,
        IAgentMemory memory,
        AgentConfig config,
        ILogger<LLMAgent> logger)
    {
        _executor = executor;
        _memory = memory;
        _config = config;
        _logger = logger;
    }

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
        sessionId ??= Guid.NewGuid().ToString("N");

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["Agent"] = _config.Name,
            ["Session"] = sessionId
        }))
        {
            var ctx = new AgentContext(sessionId, _config, input, outputType, ct);
            
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

            await foreach (var chunk in _executor.ExecuteStreamingAsync(ctx, ct))
            {
                yield return chunk;
            }
        }
    }
}
