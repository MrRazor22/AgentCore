using AgentCore.LLM.Chat;
using AgentCore.Tools;
using System.Collections.Frozen;
using System.Text.Json.Nodes;

namespace AgentCore.Tools;

/// <summary>
/// Tool permission classification. Dictates whether a tool defaults to allowed
/// or requires confirmation under policy evaluation.
/// </summary>
public enum ToolPermission
{
    /// <summary>Allowed without prompting user (e.g. ReadFile, Search, TodoList, Schedule).</summary>
    Allow,

    /// <summary>Requires host policy evaluation / user confirmation (e.g. EditFile, Filesystem, RunCommand).</summary>
    Confirm
}

/// <summary>
/// Host-configured execution policy set at agent startup.
/// </summary>
public enum ExecutionPolicy
{
    /// <summary>All Confirm-tier tools prompt user. SafeToAutoRun is strictly advisory.</summary>
    Strict,

    /// <summary>Auto-approves Confirm-tier tool if model's SafeToAutoRun=true AND guardrails pass. Otherwise prompts user.</summary>
    TrustModel,

    /// <summary>Auto-approves all permitted operations. Guardrails still enforced.</summary>
    AlwaysAllow
}

/// <summary>
/// Defense-in-depth guardrail predicate. Returns null if allowed, or denial reason if blocked.
/// </summary>
public delegate string? DenyRule(ToolCall call);

/// <summary>
/// UI abstraction for obtaining user approval for tool calls.
/// </summary>
public interface IApprovalPrompt
{
    Task<bool> RequestApprovalAsync(ToolCall call, CancellationToken ct);
}

/// <summary>
/// ToolingLayer decorator that intercepts tool calls and enforces host execution policies,
/// data-driven permission classification, call-order preservation, and defense-in-depth guardrails.
/// </summary>
public sealed class ApprovalLayer : ToolingLayer
{
    private readonly FrozenDictionary<string, ToolPermission> _permissions;
    private readonly ExecutionPolicy _policy;
    private readonly IApprovalPrompt _prompt;
    private readonly DenyRule? _guardrailDeny;
    private readonly List<Task> _inFlightEvaluations = new();
    private readonly List<ToolResult> _deniedResults = new();
    private readonly object _lock = new();

    public ApprovalLayer(
        IReadOnlyDictionary<string, ToolPermission> permissions,
        ExecutionPolicy policy,
        IApprovalPrompt prompt,
        DenyRule? guardrailDeny = null)
    {
        _permissions = (permissions ?? throw new ArgumentNullException(nameof(permissions)))
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _policy = policy;
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _guardrailDeny = guardrailDeny;
    }

    public override Task ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        var evalTask = ProcessCallAsync(call, ct);
        lock (_lock)
        {
            _inFlightEvaluations.Add(evalTask);
        }
        return evalTask;
    }

    private async Task ProcessCallAsync(ToolCall call, CancellationToken ct)
    {
        var verdict = Evaluate(call, out var reason);

        switch (verdict)
        {
            case Verdict.Allow:
                await Inner.ExecuteAsync(call, ct).ConfigureAwait(false);
                break;

            case Verdict.Prompt:
                if (await _prompt.RequestApprovalAsync(call, ct).ConfigureAwait(false))
                {
                    await Inner.ExecuteAsync(call, ct).ConfigureAwait(false);
                }
                else
                {
                    lock (_lock)
                    {
                        _deniedResults.Add(Denied(call, "User rejected execution."));
                    }
                }
                break;

            case Verdict.Deny:
            default:
                lock (_lock)
                {
                    _deniedResults.Add(Denied(call, reason ?? "Blocked by policy guardrail."));
                }
                break;
        }
    }

    public override async IAsyncEnumerable<ToolResult> StreamResultsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // 1. Wait for all in-flight evaluations to complete
        Task[] evaluationsToWait;
        lock (_lock)
        {
            evaluationsToWait = _inFlightEvaluations.ToArray();
            _inFlightEvaluations.Clear();
        }
        await Task.WhenAll(evaluationsToWait).ConfigureAwait(false);

        // 2. Yield any denied results recorded by this layer
        List<ToolResult> denied;
        lock (_lock)
        {
            denied = new List<ToolResult>(_deniedResults);
            _deniedResults.Clear();
        }

        foreach (var item in denied)
        {
            yield return item;
        }

        // 3. Stream inner results as they finish
        await foreach (var result in Inner.StreamResultsAsync(ct).ConfigureAwait(false))
        {
            yield return result;
        }
    }

    private Verdict Evaluate(ToolCall call, out string? denyReason)
    {
        // Stage 1: Guardrails check
        denyReason = _guardrailDeny?.Invoke(call);
        if (denyReason != null)
            return Verdict.Deny;

        // Stage 2: Classify (default to Confirm for unknown tools)
        var permission = _permissions.GetValueOrDefault(call.Name, ToolPermission.Confirm);

        // Stage 3: Direct Allow gate
        if (permission == ToolPermission.Allow)
            return Verdict.Allow;

        // Stage 4: ExecutionPolicy evaluation for Confirm tools
        return _policy switch
        {
            ExecutionPolicy.AlwaysAllow => Verdict.Allow,
            ExecutionPolicy.TrustModel  => ExtractSafeToAutoRun(call) ? Verdict.Allow : Verdict.Prompt,
            ExecutionPolicy.Strict      => Verdict.Prompt,
            _                           => Verdict.Prompt
        };
    }

    private static bool ExtractSafeToAutoRun(ToolCall call) =>
    call.Arguments.TryGetPropertyValue("SafeToAutoRun", out var node)
    && node is JsonValue value
    && value.TryGetValue<bool>(out var safe)
    && safe;

    private static ToolResult Denied(ToolCall call, string reason) =>
        new(call.Id, new Text($"[DENIED] {reason}"));

    private enum Verdict { Allow, Prompt, Deny }
}

/// <summary>
/// Helper factory methods for building composable defense-in-depth guardrail rules.
/// </summary>
public static class DenyRules
{
    public static DenyRule CommandPatterns(params string[] patterns) => call =>
    {
        if (!string.Equals(call.Name, "RunCommand", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!call.Arguments.TryGetPropertyValue("CommandLine", out var node))
            return null;

        var cmd = node?.GetValue<string>() ?? "";

        foreach (var pattern in patterns)
        {
            if (cmd.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return $"Blocked by defense-in-depth guardrail: command matches '{pattern}'.";
        }

        return null;
    };

    public static DenyRule Combine(params DenyRule[] rules) => call =>
    {
        foreach (var rule in rules)
        {
            var reason = rule(call);
            if (reason != null) return reason;
        }
        return null;
    };
}
