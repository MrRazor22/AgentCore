using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tooling;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AgentCore;

public interface IAgent
{
    IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        IEnumerable<IContent>? input = null,
        JsonSchema? responseSchema = null,
        CancellationToken ct = default);
}

public sealed class Agent(
    IContext context,
    ILLM llm,
    IToolbox toolbox,
    IReadOnlyList<IContent>? instructions = null,
    int maxIterations = 20) : IAgent
{
    public IContext Context => context;
    public ILLM LLM => llm;
    public IToolbox Toolbox => toolbox;
    public IReadOnlyList<IContent>? Instructions => instructions;
    public int MaxIterations => maxIterations;

    public static AgentBuilder Create() => new();

    public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        IEnumerable<IContent>? input = null,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in ResumePendingAsync(ct)) yield return evt; 
        var messages = await StageInputAsync(input, ct).ConfigureAwait(false);

        int iterations = 0;
        List<ToolCall>? toolCalls;
        do
        {
            ct.ThrowIfCancellationRequested();
            if (++iterations > maxIterations)
                throw new InvalidOperationException($"Execution exceeded maximum limit of {maxIterations} iterations.");

            toolCalls = null;
            await foreach (var evt in context.IngestAsync(
                llm.GenerateAsync(messages, responseSchema, toolbox.GetDefinitions(), ct), ct))
            {
                if (evt is ToolCall tc) (toolCalls ??= []).Add(tc);
                yield return evt;
            }

            if (toolCalls is not null)
            {
                await foreach (var evt in context.IngestAsync(
                    toolbox.ExecuteAsync(toolCalls, ct), ct))
                {
                    yield return evt;
                }

                messages = await context.PrepareAsync(ct: ct).ConfigureAwait(false);
            }
        } while (toolCalls is not null);
    }

    private async IAsyncEnumerable<IContentEvent> ResumePendingAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var messages = await context.PrepareAsync(ct: ct).ConfigureAwait(false);
        if (messages.LastOrDefault()?.Contents.LastOrDefault() is ToolCall)
        {
            var completedIds = messages
                .Where(m => m.Role == Role.Tool)
                .Select(m => m.Get<ToolCallId>()?.Value ?? m.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet();

            var pending = messages[^1].Contents
                .OfType<ToolCall>()
                .Where(c => !completedIds.Contains(c.Id))
                .ToArray();

            if (pending.Length > 0)
            {
                await foreach (var evt in context.IngestAsync(toolbox.ExecuteAsync(pending, ct), ct))
                    yield return evt;
            }
        }
    }

    private async Task<IReadOnlyList<Message>> StageInputAsync(IEnumerable<IContent>? input, CancellationToken ct)
    {
        var messages = await context.PrepareAsync(ct: ct).ConfigureAwait(false);
        List<Message> staged = [];
        if (instructions is { Count: > 0 } && !messages.Any(m => m.Role == Role.System))
            staged.Add(new(Role.System, instructions));
        if (input?.ToArray() is { Length: > 0 } user)
            staged.Add(new(Role.User, user));

        return staged.Count > 0
            ? await context.PrepareAsync(staged, ct).ConfigureAwait(false)
            : messages;
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

    public static T? FindLayer<T>(this IToolbox toolbox) where T : class
    {
        for (var t = toolbox; t != null; t = (t as ToolingLayer)?.Inner)
            if (t is T match) return match;
        return null;
    }

    public static IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        this IAgent agent,
        IContent input,
        JsonSchema? responseSchema = null,
        CancellationToken ct = default) => agent.InvokeStreamingAsync([input], responseSchema, ct);

    public static IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        this IAgent agent,
        IEnumerable<IContent>? input = null,
        CancellationToken ct = default) => agent.InvokeStreamingAsync(input, null, ct);

    public static Task<string?> InvokeAsync(
        this IAgent agent,
        IEnumerable<IContent>? input = null,
        CancellationToken ct = default) => agent.InvokeAsync<string>(input, ct);

    public static async Task<T?> InvokeAsync<T>(
        this IAgent agent,
        IEnumerable<IContent>? input = null,
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
