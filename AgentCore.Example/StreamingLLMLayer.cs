using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace AgentCore.Example;

public sealed class StreamingLLMLayer : LlmLayer
{
    private readonly Action<LLMEvent> _onEvent;

    public StreamingLLMLayer(Action<LLMEvent> onEvent)
    {
        _onEvent = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
    }

    public override async IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in base.StreamAsync(messages, options, tools, ct).WithCancellation(ct).ConfigureAwait(false))
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
