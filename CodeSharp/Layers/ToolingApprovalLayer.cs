using AgentCore.LLM.Chat;
using AgentCore.Tools;
using System.Collections.Frozen;
using System.Text.Json.Nodes;

namespace CodeSharp.Layers;

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

    public ApprovalLayer(
        ITooling inner,
        IReadOnlyDictionary<string, ToolPermission> permissions,
        ExecutionPolicy policy,
        IApprovalPrompt prompt,
        DenyRule? guardrailDeny = null) : base(inner)
    {
        _permissions = (permissions ?? throw new ArgumentNullException(nameof(permissions)))
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _policy = policy;
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _guardrailDeny = guardrailDeny;
    }

    public override async Task<IReadOnlyList<ToolResult>> ExecuteAsync(
        IEnumerable<ToolCall> calls, CancellationToken ct = default)
    {
        var callList = calls as IReadOnlyList<ToolCall> ?? calls.ToList();
        var results = new ToolResult[callList.Count];

        var approvedIndices = new List<int>();
        var approvedCalls = new List<ToolCall>();

        for (int i = 0; i < callList.Count; i++)
        {
            var call = callList[i];
            var verdict = Evaluate(call, out var reason);

            switch (verdict)
            {
                case Verdict.Allow:
                    approvedIndices.Add(i);
                    approvedCalls.Add(call);
                    break;

                case Verdict.Prompt:
                    if (await _prompt.RequestApprovalAsync(call, ct).ConfigureAwait(false))
                    {
                        approvedIndices.Add(i);
                        approvedCalls.Add(call);
                    }
                    else
                    {
                        results[i] = Denied(call, "User rejected execution.");
                    }
                    break;

                case Verdict.Deny:
                    results[i] = Denied(call, reason ?? "Blocked by policy guardrail.");
                    break;
            }
        }

        if (approvedCalls.Count > 0)
        {
            var executedResults = await Inner.ExecuteAsync(approvedCalls, ct).ConfigureAwait(false);
            for (int j = 0; j < approvedCalls.Count; j++)
            {
                results[approvedIndices[j]] = j < executedResults.Count
                    ? executedResults[j]
                    : Denied(approvedCalls[j], "Tool execution produced no result.");
            }
        }

        return results;
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
