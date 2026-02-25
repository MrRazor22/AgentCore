using AgentCore.Chat;
using AgentCore.LLM.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentCore.Tokens;

public sealed class ContextBudgetOptions
{
    public int MaxContextTokens { get; set; } = 8000;
    public double Margin { get; set; } = 0.6;
    public int KeepLastMessages { get; set; } = 4;
}

public interface IContextManager
{
    IList<Message> Trim(IList<Message> reqPrompt, int? requiredGap = null);
}

public sealed class ContextManager(IOptions<ContextBudgetOptions> options, ITokenManager _tokenManager, ILogger<ContextManager> _logger) : IContextManager
{
    private readonly ContextBudgetOptions _opts = options.Value;

    public IList<Message> Trim(IList<Message> reqPrompt, int? requiredGap = null)
    {
        if (reqPrompt == null) throw new ArgumentNullException(nameof(reqPrompt));

        if (requiredGap.HasValue)
        {
            if (requiredGap.Value < 0) throw new ArgumentOutOfRangeException(nameof(requiredGap), "Required gap cannot be negative.");
            int maxAllowedGap = (int)(_opts.MaxContextTokens * (1 - _opts.Margin));
            if (requiredGap.Value > maxAllowedGap)
                throw new ArgumentOutOfRangeException(nameof(requiredGap), $"Required gap {requiredGap.Value} exceeds allowed maximum {maxAllowedGap} tokens based on margin.");
        }

        var source = reqPrompt.Clone();
        int originalCount = _tokenManager.AppromimateCount(source.ToJson());

        int limit = requiredGap.HasValue
            ? Math.Max(0, _opts.MaxContextTokens - requiredGap.Value)
            : (int)(_opts.MaxContextTokens * _opts.Margin);

        var system = source.Where(m => m.Role == Role.System).ToList();
        Message? lastToolMsg = source.LastOrDefault(m => m.Role == Role.Tool);
        ToolCall? lastToolCall = (lastToolMsg?.Content as ToolResult)?.Call;

        var ua = source.Where(m => (m.Role == Role.User || m.Role == Role.Assistant) && m.Content is Text).ToList();

        Message? lastUser = null, lastAssistant = null;
        for (int i = ua.Count - 1; i >= 0; i--)
        {
            if (lastUser == null && ua[i].Role == Role.User) lastUser = ua[i];
            else if (lastUser != null && ua[i].Role == Role.Assistant) { lastAssistant = ua[i]; break; }
        }

        var keepUA = ua.Skip(Math.Max(0, ua.Count - _opts.KeepLastMessages)).ToList();
        if (lastUser != null && !keepUA.Contains(lastUser)) keepUA.Add(lastUser);
        if (lastAssistant != null && !keepUA.Contains(lastAssistant)) keepUA.Add(lastAssistant);

        keepUA = keepUA.Distinct().OrderBy(m => source.IndexOf(m)).ToList();

        IList<Message> Build(IReadOnlyList<Message> uaSlice)
        {
            var c = new List<Message>();
            foreach (var s in system) c.Add(new Message(s.Role, s.Content));
            if (lastToolCall != null) c.AddAssistantToolCall(lastToolCall);
            if (lastToolMsg != null) c.Add(new Message(lastToolMsg.Role, lastToolMsg.Content));
            foreach (var m in uaSlice) c.Add(new Message(m.Role, m.Content));
            return c;
        }

        IList<Message> result;
        if (originalCount <= limit)
        {
            result = source;
        }
        else
        {
            var rebuilt = Build(keepUA);
            int finalCount = _tokenManager.AppromimateCount(rebuilt.ToJson());

            while (finalCount > limit && keepUA.Count > 0)
            {
                keepUA.RemoveAt(0);
                rebuilt = Build(keepUA);
                finalCount = _tokenManager.AppromimateCount(rebuilt.ToJson());
            }
            result = rebuilt;
        }

        int finalTokens = _tokenManager.AppromimateCount(result.ToJson());
        double usagePct = Math.Round((double)finalTokens / _opts.MaxContextTokens * 100, 1);

        _logger.LogDebug("ContextBudget Original={Original}, Final={Final}, Removed={Removed}, UsagePct={UsagePct}",
            originalCount, finalTokens, originalCount - finalTokens, usagePct);

        return result;
    }
}
