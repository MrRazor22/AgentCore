using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCoreT06;

public record ValidationDiagnostic(
    string Target,
    string RuleName,
    string ViolatingValue,
    string Reason,
    DateTimeOffset Timestamp);

public class ValidationException(string message, ValidationDiagnostic diagnostic) : Exception(message)
{
    public ValidationDiagnostic Diagnostic { get; } = diagnostic;
}

public sealed class InputGuardrailLLMLayer(
    Func<string, (bool IsValid, string Rule, string Reason)> promptValidator,
    Action<ValidationDiagnostic>? onDiagnostic = null,
    ILLM? inner = null) : LLMLayer(inner)
{
    private readonly Func<string, (bool IsValid, string Rule, string Reason)> _validator =
        promptValidator ?? throw new ArgumentNullException(nameof(promptValidator));
    private readonly Action<ValidationDiagnostic>? _onDiagnostic = onDiagnostic;

    public override async IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                var text = content is Text t ? t.Value : content.ToString() ?? string.Empty;
                var (isValid, rule, reason) = _validator(text);
                if (!isValid)
                {
                    var diag = new ValidationDiagnostic("Prompt", rule, text, reason, DateTimeOffset.UtcNow);
                    _onDiagnostic?.Invoke(diag);
                    throw new ValidationException($"Input validation rejected prompt: {reason}", diag);
                }
            }
        }

        await foreach (var evt in Inner.GenerateAsync(messages, tools, responseSchema, ct).ConfigureAwait(false))
        {
            yield return evt;
        }
    }
}

public sealed class ToolArgumentGuardrailLayer(
    Func<ToolCall, (bool IsValid, string Rule, string Reason)> toolValidator,
    Action<ValidationDiagnostic>? onDiagnostic = null,
    IToolbox? inner = null) : ToolboxLayer(inner)
{
    private readonly Func<ToolCall, (bool IsValid, string Rule, string Reason)> _validator =
        toolValidator ?? throw new ArgumentNullException(nameof(toolValidator));
    private readonly Action<ValidationDiagnostic>? _onDiagnostic = onDiagnostic;

    public override async IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var call in calls)
        {
            var (isValid, rule, reason) = _validator(call);
            if (!isValid)
            {
                var diag = new ValidationDiagnostic(call.Name, rule, call.Arguments, reason, DateTimeOffset.UtcNow);
                _onDiagnostic?.Invoke(diag);
                throw new ValidationException($"Tool argument validation failed for '{call.Name}': {reason}", diag);
            }
        }

        await foreach (var evt in base.ExecuteAsync(calls, ct).ConfigureAwait(false))
        {
            yield return evt;
        }
    }
}
