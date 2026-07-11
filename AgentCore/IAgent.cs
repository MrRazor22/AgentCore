using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Schema;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace AgentCore;

public interface IAgent
{
    Task<T?> InvokeAsync<T>(IContent input, CancellationToken ct = default);
    IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(IContent input, CancellationToken ct = default);
}

public sealed record AgentServices(
    ILLM Llm,
    ITooling Tooling,
    IMemory Memory,
    ITokenCounter TokenCounter,
    ILLMProvider Provider,
    ILoggerFactory LoggerFactory);
 

public sealed class Agent : IAgent
{
    private readonly AgentServices _services;
    private readonly LLMOptions _options;
    private readonly IContent? _instructions;
    private readonly IAgentExecutor _executor;
    private readonly ILogger<Agent> _logger;

    public Agent(
        AgentServices services,
        LLMOptions options,
        IContent? instructions,
        IAgentExecutor executor)
    {
        _services = services;
        _options = options;
        _instructions = instructions;
        _executor = executor;
        _logger = services.LoggerFactory.CreateLogger<Agent>();
    } 

    public async Task<T?> InvokeAsync<T>(IContent input, CancellationToken ct = default)
    {
        JsonSchema? schema = null;
        if (typeof(T) != typeof(string))
        {
            schema = typeof(T).GetSchemaForType();
        }

        string rawText = "";

        await foreach (var evt in ExecuteStreamAsync(input, schema, ct))
        {
            if (evt is ErrorEvent error)
            {
                throw error.Error;
            }
            if (evt is AgentResponseEvent resp)
            {
                rawText = resp.Response;
            }
        }
        
        if (typeof(T) == typeof(string))
        {
            return (T?)(object)rawText;
        }

        if (string.IsNullOrWhiteSpace(rawText)) return default;
        return System.Text.Json.JsonSerializer.Deserialize<T>(rawText);
    }

    public IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        IContent input,
        CancellationToken ct = default)
    {
        return ExecuteStreamAsync(input, null, ct);
    }

    private async IAsyncEnumerable<AgentEvent> ExecuteStreamAsync(
        IContent input,
        JsonSchema? responseSchema,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var scope = _logger.BeginScope(new[]
        {
            new KeyValuePair<string, object?>("Agent", nameof(Agent))
        });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Agent invoked. InputLength={InputLength} ContextLimit={ContextLimit}",
            input.ForLlm().Length, _options.ContextWindow ?? 0);

        var userMessage = new Message(Role.User, input);

        IReadOnlyList<Message> recalledChat;
        try
        {
            recalledChat = await _services.Memory.RecallAsync(
                userMessage,
                _options.ContextWindow,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent failed during memory recall. DurationMs={DurationMs}", stopwatch.ElapsedMilliseconds);
            throw;
        }

        var conversation = new List<Message>();
        if (_instructions != null)
        {
            conversation.Add(new Message(Role.System, _instructions));
        }

        conversation.AddRange(recalledChat);

        var rememberFrom = conversation.Count;
        conversation.Add(userMessage);

        await foreach (var evt in _executor.ExecuteAsync(conversation, responseSchema, ct))
        {
            yield return evt;
        }

        try
        {
            var newTurnMessages = conversation.Skip(rememberFrom).ToList();
            await _services.Memory.RememberAsync(newTurnMessages, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent failed during memory remember. DurationMs={DurationMs}", stopwatch.ElapsedMilliseconds);
            throw;
        }

        _logger.LogInformation("Agent completed. DurationMs={DurationMs}", stopwatch.ElapsedMilliseconds);
    }
}


