using AgentCore.Chat;
using AgentCore.Providers;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace AgentCore.LLM;

public interface ILLMExecutor
{
    IAsyncEnumerable<IContentDelta> StreamAsync(
        IReadOnlyList<Message> messages, 
        LLMOptions options, 
        CancellationToken ct = default);
}

public class LLMExecutor(
    ILLMProvider _provider,
    IToolCatalog _toolCatalog,
    IContextManager _ctxManager,
    ILogger<LLMExecutor> _logger
) : ILLMExecutor
{
    public async IAsyncEnumerable<IContentDelta> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var trimmed = _ctxManager.Trim([.. messages], options.MaxOutputTokens);
        IReadOnlyList<Message> trimmedList = [.. trimmed];

        var tools = _toolCatalog.RegisteredTools;

        _logger.LogTrace("LLM request: {Model} {Options}", options.Model, options);

        var (content, metaTask) = _provider.StreamAsync(trimmedList, options, tools, ct);

        await foreach (var delta in content.WithCancellation(ct))
        {
            yield return delta;
        }

        var meta = await metaTask;
        sw.Stop();
        _logger.LogDebug("LLM call finished: {FinishReason} Duration={Ms}ms", meta.FinishReason, sw.ElapsedMilliseconds);
        if (meta.TokenUsage != null)
            _logger.LogTrace("Token usage: In={In} Out={Out}", meta.TokenUsage.InputTokens, meta.TokenUsage.OutputTokens);
    }
}
