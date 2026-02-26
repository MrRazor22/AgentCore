using AgentCore.Chat;
using AgentCore.Json;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.LLM;

public interface ILLMExecutor
{
    IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        CancellationToken ct = default);
}

public class LLMExecutor(
    ILLMProvider _provider,
    IToolRegistry _toolRegistry,
    IContextManager _ctxManager,
    ITokenManager _tokenManager,
    ILogger<LLMExecutor> _logger
) : ILLMExecutor
{
    public Func<IReadOnlyList<Message>, LLMOptions, CancellationToken, Task<IReadOnlyList<LLMEvent>?>>? BeforeCall { get; init; }
    public Func<IReadOnlyList<LLMEvent>, CancellationToken, Task>? AfterCall { get; init; }

    public async IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var reduced = _ctxManager.Reduce([.. messages], options);
        IReadOnlyList<Message> reducedList = [.. reduced];

        // Before hook — non-null return short-circuits (e.g. cached response)
        if (BeforeCall != null)
        {
            var cached = await BeforeCall(reducedList, options, ct).ConfigureAwait(false);
            if (cached != null)
            {
                foreach (var evt in cached)
                    yield return evt;

                if (AfterCall != null)
                    await AfterCall(cached, ct).ConfigureAwait(false);

                yield break;
            }
        }

        var tools = _toolRegistry.Tools;

        _logger.LogTrace("LLM request: {Model} {Options}", options.Model, options);

        var content = _provider.StreamAsync(reducedList, options, tools, ct);

        var toolCalls = new Dictionary<int, (string id, string name, StringBuilder args)>();
        TokenUsage? tokenUsage = null;
        FinishReason? finishReason = null;
        var collectedEvents = new List<LLMEvent>();

        await foreach (var delta in content.WithCancellation(ct))
        {
            switch (delta)
            {
                case TextDelta t:
                    var textEvt = new TextEvent(t.Value);
                    collectedEvents.Add(textEvt);
                    yield return textEvt;
                    break;

                case ToolCallDelta tc:
                    if (!toolCalls.TryGetValue(tc.Index, out var entry))
                        entry = ("", "", new StringBuilder());
                    if (!string.IsNullOrEmpty(tc.Id)) entry.id = tc.Id;
                    if (!string.IsNullOrEmpty(tc.Name)) entry.name = tc.Name;
                    if (!string.IsNullOrEmpty(tc.ArgumentsDelta)) entry.args.Append(tc.ArgumentsDelta);
                    toolCalls[tc.Index] = entry;
                    break;

                case MetaDelta m:
                    tokenUsage = m.TokenUsage;
                    finishReason = m.FinishReason;
                    break;
            }
        }

        foreach (var (_, (id, name, args)) in toolCalls.OrderBy(kv => kv.Key))
        {
            var argsStr = args.ToString();
            JsonObject parsedArgs = argsStr.TryParseCompleteJson(out var parsed)
                ? parsed ?? new JsonObject()
                : new JsonObject();

            var tcEvt = new ToolCallEvent(new ToolCall(id, name, parsedArgs));
            collectedEvents.Add(tcEvt);
            yield return tcEvt;
        }

        // Record token usage for per-session tracking
        if (tokenUsage != null)
            _tokenManager.Record(tokenUsage);

        // After hook — observe-only
        if (AfterCall != null)
            await AfterCall(collectedEvents, ct).ConfigureAwait(false);

        sw.Stop();
        _logger.LogDebug("LLM call finished: {FinishReason} Duration={Ms}ms", finishReason, sw.ElapsedMilliseconds);
        if (tokenUsage != null)
            _logger.LogTrace("Token usage: In={In} Out={Out}", tokenUsage.InputTokens, tokenUsage.OutputTokens);
    }
}
