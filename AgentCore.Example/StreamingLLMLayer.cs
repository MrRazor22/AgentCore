using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace AgentCore.Example;

public sealed class StreamingLLMLayer : ILLM
{
    private readonly ILLM _inner;
    private readonly Action<LLMEvent> _onEvent;

    public StreamingLLMLayer(ILLM inner, Action<LLMEvent> onEvent)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _onEvent = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
    }

    public LLMCapabilities GetCapabilities() => _inner.GetCapabilities();

    public async IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in _inner.StreamAsync(messages, options, tools, ct).WithCancellation(ct).ConfigureAwait(false))
        {
            try
            {
                _onEvent(evt);
            }
            catch
            {
                // Prevent callback exceptions from interrupting the stream
            }
            yield return evt;
        }
    }
}
