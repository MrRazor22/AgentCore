using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using System.Runtime.CompilerServices;
using System.Text;

namespace AgentCore;

public interface IAgent
{
    Task<string?> InvokeAsync(IContent input, CancellationToken ct = default);
    Task<T?> InvokeAsync<T>(IContent input, CancellationToken ct = default);
    IAsyncEnumerable<IAgentEvent> InvokeStreamingAsync(IContent input, CancellationToken ct = default);
}

public sealed partial class Agent(IContext context, IAgentWorkflow workflow) : IAgent
{
    public static Builder Create() => new();

    public Task<string?> InvokeAsync(IContent input, CancellationToken ct = default) =>
        InvokeAsync<string>(input, ct);

    public async Task<T?> InvokeAsync<T>(IContent input, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var schema = typeof(T) == typeof(string) ? null : typeof(T).GetSchemaForType();

        await foreach (var evt in ExecuteStreamAsync(input, schema, ct))
        {
            if (evt is TextDelta td) sb.Append(td.Text);
        }

        var text = sb.ToString();
        if (typeof(T) == typeof(string)) return (T?)(object)text;
        return string.IsNullOrWhiteSpace(text) ? default : System.Text.Json.JsonSerializer.Deserialize<T>(text);
    }

    public IAsyncEnumerable<IAgentEvent> InvokeStreamingAsync(IContent input, CancellationToken ct = default) =>
        ExecuteStreamAsync(input, null, ct);

    private async IAsyncEnumerable<IAgentEvent> ExecuteStreamAsync(
        IContent input,
        JsonSchema? responseSchema,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in workflow.ExecuteAsync(context, input, responseSchema, ct))
        {
            yield return evt;
        }
    }
}
