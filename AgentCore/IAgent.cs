using AgentCore.LLM;
using AgentCore.Context;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using AgentCore.LLM.Schema;
using AgentCore.LLM.Chat;
using System.Text;
using System.Linq;

namespace AgentCore;

public interface IAgent
{
    Task<string?> InvokeAsync(IContent input, CancellationToken ct = default);
    Task<T?> InvokeAsync<T>(IContent input, CancellationToken ct = default);
    IAsyncEnumerable<IContent> InvokeStreamingAsync(IContent input, CancellationToken ct = default);
}

public sealed partial class Agent : IAgent
{
    public static Builder Create() => new Builder();

    private readonly ILLM _llm; 
    private readonly IContext _memory;
    private readonly IReadOnlyList<Tool> _tools;
    private readonly IContent? _instructions;
    private readonly IAgentWorkflow _workflow;
    private readonly ILogger<Agent> _logger;

    public Agent(
        ILLM llm, 
        IContext memory,
        IReadOnlyList<Tool> tools,
        IContent? instructions,
        IAgentWorkflow workflow,
        ILogger<Agent>? logger = null)
    {
        _llm = llm; 
        _memory = memory;
        _tools = tools;
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
        var sb = new StringBuilder();
        await foreach (var content in InvokeStreamingAsyncInternal<T>(input, ct))
        {
            if (content is Text t)
            {
                sb.Append(t.Value);
            }
        }
        
        var fullText = sb.ToString();
        return Deserialize<T>(fullText);
    }

    public IAsyncEnumerable<IContent> InvokeStreamingAsync(
        IContent input,
        CancellationToken ct = default)
    {
        return ExecuteStreamAsync(input, null, ct);
    }

    private IAsyncEnumerable<IContent> InvokeStreamingAsyncInternal<T>(
        IContent input,
        CancellationToken ct = default)
    {
        JsonSchema? schema = null;
        if (typeof(T) != typeof(string))
        {
            schema = typeof(T).GetSchemaForType();
        }

        return ExecuteStreamAsync(input, schema, ct);
    }

    private async IAsyncEnumerable<IContent> ExecuteStreamAsync(
        IContent input,
        JsonSchema? responseSchema,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var scope = _logger.BeginScope(new[]
        {
            new KeyValuePair<string, object?>("Agent", nameof(Agent))
        }); 

        await foreach (var content in _workflow.ExecuteAsync(_memory, input, responseSchema, ct))
        {
            yield return content;
        }

        _logger.LogInformation("Agent completed.");
    }
}
