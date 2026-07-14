using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.LLM;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using AgentCore.LLM.Schema;
using AgentCore.LLM.Chat;

namespace AgentCore;

public interface IAgent
{
    Task<string?> InvokeAsync(IContent input, CancellationToken ct = default);
    IAsyncEnumerable<AgentEvent> InvokeStreamingAsync<T>(IContent input, CancellationToken ct = default);
    Task<T?> InvokeAsync<T>(IContent input, CancellationToken ct = default);
    IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(IContent input, CancellationToken ct = default);
}

public sealed partial class Agent : IAgent
{
    public static Builder Create() => new Builder();

    private readonly ILLMService _llm;
    private readonly IToolService _tooling;
    private readonly IMemoryService _memory;
    private readonly IContent? _instructions;
    private readonly IAgentWorkflow _workflow;
    private readonly ILogger<Agent> _logger;

    public Agent(
        ILLMService llm,
        IToolService tooling,
        IMemoryService memory,
        IContent? instructions,
        IAgentWorkflow workflow,
        ILogger<Agent>? logger = null)
    {
        _llm = llm;
        _tooling = tooling;
        _memory = memory;
        _instructions = instructions;
        _workflow = workflow;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<Agent>.Instance;
    } 

    private static T? Deserialize<T>(string? response)
    {
        if (typeof(T) == typeof(string))
        {
            return (T?)(object)(response ?? "");
        }
        if (string.IsNullOrWhiteSpace(response)) return default;
        return System.Text.Json.JsonSerializer.Deserialize<T>(response);
    }

    public Task<string?> InvokeAsync(IContent input, CancellationToken ct = default)
    {
        return InvokeAsync<string>(input, ct);
    }

    public async Task<T?> InvokeAsync<T>(IContent input, CancellationToken ct = default)
    {
        T? result = default;
        await foreach (var evt in InvokeStreamingAsync<T>(input, ct))
        {
            if (evt is ErrorEvent error)
            {
                throw error.Error;
            }
            if (evt is AgentResponseEvent<T> resp)
            {
                result = resp.Response;
            }
        }
        
        return result;
    }

    public IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        IContent input,
        CancellationToken ct = default)
    {
        return ExecuteStreamAsync(input, null, ct);
    }

    public async IAsyncEnumerable<AgentEvent> InvokeStreamingAsync<T>(
        IContent input,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        JsonSchema? schema = null;
        if (typeof(T) != typeof(string))
        {
            schema = typeof(T).GetSchemaForType();
        }

        await foreach (var evt in ExecuteStreamAsync(input, schema, ct))
        {
            if (evt is AgentResponseEvent<string> resp)
            {
                yield return new AgentResponseEvent<T>(Deserialize<T>(resp.Response)!);
            }
            else
            {
                yield return evt;
            }
        }
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

        var userMessage = new Message(Role.User, input);

        var fixedMessages = new List<Message>();
        if (_instructions != null)
        {
            fixedMessages.Add(new Message(Role.System, _instructions));
        }
        fixedMessages.Add(userMessage);

        IReadOnlyList<Message> recalledChat;
        try
        {
            int tokenBudget = await _llm.GetContextBudgetAsync(fixedMessages, ct).ConfigureAwait(false);
            _logger.LogInformation("Agent invoked. InputLength={InputLength} MemoryBudget={MemoryBudget}",
                input.ForLlm().Length, tokenBudget);

            recalledChat = await _memory.RecallAsync(
                userMessage,
                tokenBudget,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)    
        {
            _logger.LogError(ex, "Agent failed during memory recall.");
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

        await foreach (var evt in _workflow.ExecuteAsync(conversation, responseSchema, ct))
        {
            yield return evt;
        }

        try
        {
            var newTurnMessages = conversation.Skip(rememberFrom).ToList();
            await _memory.RememberAsync(newTurnMessages, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent failed during memory remember.");
            throw;
        }

        _logger.LogInformation("Agent completed.");
    }
}



