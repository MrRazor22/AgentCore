using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AgentCore;

public interface IAgent
{
    IAsyncEnumerable<IAgentEvent> InvokeStreamingAsync(IContent input, JsonSchema? responseSchema = null, CancellationToken ct = default);
}

public sealed class Agent(
    IContext context,
    ILLM llm,
    ITooling tooling,
    IReadOnlyList<IContent>? instructions = null,
    int maxIterations = 20) : IAgent
{
    public IContext Context => context;
    public ILLM LLM => llm;
    public ITooling Tooling => tooling;
    public IReadOnlyList<IContent>? Instructions => instructions;
    public int MaxIterations => maxIterations;

    public static AgentBuilder Create() => new();

    public async IAsyncEnumerable<IAgentEvent> InvokeStreamingAsync(
        IContent input,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var existing = await context.GetAsync(ct);
        if (instructions is { Count: > 0 } && !existing.Any(m => m.Role == Role.System))
        {
            await context.AppendAsync(new Message(Role.System, instructions), ct);
        }
        await context.AppendAsync(new Message(Role.User, [input]), ct);

        for (int i = 0; i < maxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();
            var messages = await context.GetAsync(ct);

            await foreach (var evt in llm.StreamAsync(messages, responseSchema, tooling.GetDefinitions(), ct))
            {
                yield return evt;
                await context.AppendAsync(evt, ct);
            }

            var currentHistory = await context.GetAsync(ct);
            var lastAssistant = currentHistory.LastOrDefault(m => m.Role == Role.Assistant);
            if (lastAssistant == null || lastAssistant.Contents.Count == 0) yield break;

            var toolCalls = lastAssistant.Contents
                .OfType<ToolCall>()
                .DistinctBy(tc => tc.Id)
                .ToList();

            if (toolCalls.Count == 0) yield break;

            await foreach (var evt in tooling.ExecuteStreamingAsync(toolCalls, ct))
            {
                yield return evt;
                await context.AppendAsync(evt, ct);
            }
        }

        throw new InvalidOperationException($"Execution exceeded maximum limit of {maxIterations} iterations.");
    }
}

public static class AgentExtensions
{
    public static IAsyncEnumerable<IAgentEvent> InvokeStreamingAsync(
        this Agent agent,
        IContent input,
        CancellationToken ct) => agent.InvokeStreamingAsync(input, null, ct);

    public static Task<string?> InvokeAsync(
        this Agent agent,
        IContent input,
        CancellationToken ct = default) => agent.InvokeAsync<string>(input, ct);

    public static async Task<T?> InvokeAsync<T>(
        this Agent agent,
        IContent input,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var schema = typeof(T) == typeof(string) ? null : typeof(T).GetSchemaForType();

        await foreach (var evt in agent.InvokeStreamingAsync(input, schema, ct))
        {
            if (evt is TextDelta td) sb.Append(td.Text);
        }

        var text = sb.ToString();
        if (typeof(T) == typeof(string)) return (T?)(object)text;
        return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text);
    }
}
