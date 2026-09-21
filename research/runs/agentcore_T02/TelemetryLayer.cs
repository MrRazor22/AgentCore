using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCoreT02;

public record TelemetryRecord(
    string Category,
    string Phase,
    DateTimeOffset Timestamp,
    TimeSpan? Duration,
    int? Tokens,
    string? CallerId,
    string? Status,
    string? SanitizedPayload);

public static class SecretSanitizer
{
    private static readonly Regex SecretRegex = new(
        @"(?i)(api[_-]?key|password|secret|bearer\s+|token)["":\s=]+([A-Za-z0-9_\-\.]{8,})",
        RegexOptions.Compiled);

    public static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return SecretRegex.Replace(input, "$1: [REDACTED]");
    }
}

public sealed class TelemetryLLMLayer(
    ILLM? inner = null,
    Action<TelemetryRecord>? onEvent = null,
    string? callerId = "default_caller") : LLMLayer(inner)
{
    private readonly Action<TelemetryRecord>? _onEvent = onEvent;
    private readonly string? _callerId = callerId;

    public override async IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var rawPayload = string.Join(" ", messages.Select(m => string.Join(" ", m.Contents.Select(c => c is Text t ? t.Value : c.ToString()))));
        var sanitizedPayload = SecretSanitizer.Sanitize(rawPayload);

        _onEvent?.Invoke(new TelemetryRecord(
            "LLM",
            "Start",
            DateTimeOffset.UtcNow,
            null,
            null,
            _callerId,
            "Started",
            sanitizedPayload));

        var sw = Stopwatch.StartNew();
        var tokenCount = 0;
        var hasError = false;

        IAsyncEnumerator<IMessageEvent>? enumerator = null;
        try
        {
            enumerator = Inner.GenerateAsync(messages, tools, responseSchema, ct).GetAsyncEnumerator(ct);

            while (true)
            {
                bool hasNext;
                IMessageEvent? item = null;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    if (hasNext) item = enumerator.Current;
                }
                catch
                {
                    hasError = true;
                    throw;
                }

                if (!hasNext) break;

                if (item is Message m && m.Metadata.Get<TokenUsage>() is { } usage)
                {
                    tokenCount = usage.TotalTokens;
                }
                else if (item is MessageDelta md && md.Content is TextDelta td)
                {
                    tokenCount += Math.Max(1, td.Text.Length / 4);
                }
                else if (item is MessageDelta md2 && md2.Content is Text t)
                {
                    tokenCount += Math.Max(1, t.Value.Length / 4);
                }

                yield return item!;
            }
        }
        finally
        {
            sw.Stop();
            if (enumerator != null) await enumerator.DisposeAsync().ConfigureAwait(false);

            _onEvent?.Invoke(new TelemetryRecord(
                "LLM",
                "End",
                DateTimeOffset.UtcNow,
                sw.Elapsed,
                tokenCount,
                _callerId,
                hasError ? "Error" : "Success",
                null));
        }
    }
}

public sealed class TelemetryToolboxLayer(
    IToolbox? inner = null,
    Action<TelemetryRecord>? onEvent = null,
    string? callerId = "default_caller") : ToolboxLayer(inner)
{
    private readonly Action<TelemetryRecord>? _onEvent = onEvent;
    private readonly string? _callerId = callerId;

    public override async IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var rawPayload = string.Join("; ", calls.Select(c => $"{c.Name}:{c.Arguments}"));
        var sanitized = SecretSanitizer.Sanitize(rawPayload);

        _onEvent?.Invoke(new TelemetryRecord(
            "Tool",
            "Start",
            DateTimeOffset.UtcNow,
            null,
            null,
            _callerId,
            "Started",
            sanitized));

        var sw = Stopwatch.StartNew();
        var hasError = false;

        IAsyncEnumerator<IMessageEvent>? enumerator = null;
        try
        {
            enumerator = Inner.ExecuteAsync(calls, ct).GetAsyncEnumerator(ct);

            while (true)
            {
                bool hasNext;
                IMessageEvent? item = null;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    if (hasNext) item = enumerator.Current;
                }
                catch
                {
                    hasError = true;
                    throw;
                }

                if (!hasNext) break;

                if (item is MessageDelta md && md.Content is ToolResult tr && tr.IsError)
                {
                    hasError = true;
                }

                yield return item!;
            }
        }
        finally
        {
            sw.Stop();
            if (enumerator != null) await enumerator.DisposeAsync().ConfigureAwait(false);

            _onEvent?.Invoke(new TelemetryRecord(
                "Tool",
                "End",
                DateTimeOffset.UtcNow,
                sw.Elapsed,
                null,
                _callerId,
                hasError ? "Failed" : "Success",
                null));
        }
    }
}
