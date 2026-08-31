using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;

namespace AgentCore.LLM;

public sealed class StreamingLLMLayer : LLMLayer
{


    public ChannelWriter<object>? Writer { get; set; }

    public override IAsyncEnumerable<IMessageEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        CancellationToken ct = default)
    {
        return InterceptEventsAsync(Inner.StreamAsync(messages, responseSchema, tools, ct), ct);
    }

    private async IAsyncEnumerable<IMessageEvent> InterceptEventsAsync(
        IAsyncEnumerable<IMessageEvent> innerEvents,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var writer = Writer;
        await foreach (var evt in innerEvents.WithCancellation(ct).ConfigureAwait(false))
        {
            writer?.TryWrite(evt);
            yield return evt;
        }
    }
}
