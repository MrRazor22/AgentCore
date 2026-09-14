using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AgentCore;

public interface IAgent
{
    IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(IContent input, JsonSchema? responseSchema = null, CancellationToken ct = default);
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

    public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        IContent input,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var existing = await context.PrepareAsync(ct: ct).ConfigureAwait(false);
        IEnumerable<Message>? staged = instructions is { Count: > 0 } && !existing.Any(m => m.Role == Role.System)
            ? [new Message(Role.System, instructions), new Message(Role.User, [input])]
            : [new Message(Role.User, [input])];

        for (int i = 0; i < maxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();
            var messages = await context.PrepareAsync(staged, ct);
            staged = null;

            List<ToolCall>? toolCalls = null;
            await foreach (var evt in context.IngestAsync(
                llm.GenerateAsync(messages, responseSchema, tooling.GetDefinitions(), ct), ct))
            {
                if (evt is ToolCall tc) (toolCalls ??= []).Add(tc);
                yield return evt;
            }

            if (toolCalls is not { Count: > 0 }) yield break;

            await foreach (var evt in context.IngestAsync(
                tooling.ExecuteAsync(toolCalls, ct), ct))
            {
                yield return evt;
            }
        }

        throw new InvalidOperationException($"Execution exceeded maximum limit of {maxIterations} iterations.");
    }
}

public static class AgentExtensions
{
    public static T? FindLayer<T>(this IContext context) where T : class
    {
        for (var c = context; c != null; c = (c as ContextLayer)?.Inner)
            if (c is T match) return match;
        return null;
    }

    public static T? FindLayer<T>(this ILLM llm) where T : class
    {
        for (var l = llm; l != null; l = (l as LLMLayer)?.Inner)
            if (l is T match) return match;
        return null;
    }

    public static T? FindLayer<T>(this ITooling tooling) where T : class
    {
        for (var t = tooling; t != null; t = (t as ToolingLayer)?.Inner)
            if (t is T match) return match;
        return null;
    }

    public static IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
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
        bool hasDeltas = false;

        await foreach (var evt in agent.InvokeStreamingAsync(input, schema, ct))
        {
            if (evt is TextDelta td) { sb.Append(td.Text); hasDeltas = true; }
            else if (evt is Text t && !hasDeltas) sb.Append(t.Value);
        }

        var text = sb.ToString();
        if (typeof(T) == typeof(string)) return (T?)(object)text;
        return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text);
    }
}
