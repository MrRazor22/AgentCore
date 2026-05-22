using AgentCore.Conversation;
using AgentCore.Tooling;
using System;
using System.Collections.Generic;

namespace AgentCore.LLM;

/// <summary>
/// A lightweight internal wrapper that binds invocation-specific LLMOptions and
/// Tools to an ILLMExecutor, keeping the orchestration loop signature clean.
/// </summary>
internal sealed class ConfiguredLLMExecutor : ILLMExecutor
{
    private readonly ILLMExecutor _inner;
    private readonly LLMOptions _options;
    private readonly IReadOnlyList<Tool>? _tools;

    public ConfiguredLLMExecutor(ILLMExecutor inner, LLMOptions options, IReadOnlyList<Tool>? tools)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = tools;
    }

    public IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        CancellationToken ct = default)
    {
        // Ignores options/tools passed at call time, using the pre-bound ones
        return _inner.StreamAsync(messages, _options, _tools, ct);
    }
}
