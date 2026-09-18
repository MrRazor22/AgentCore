using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tooling;

namespace AgentCore.LLM;

public sealed class StreamingEventLayer<T>(Func<IMessageEvent, T>? mapper = null) : LLMLayer
{
    public ChannelWriter<T>? Writer { get; set; }

    public override IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        CancellationToken ct = default)
    {
        return InterceptEventsAsync(Inner.GenerateAsync(messages, tools, responseSchema, ct), ct);
    }

    private async IAsyncEnumerable<IMessageEvent> InterceptEventsAsync(
        IAsyncEnumerable<IMessageEvent> innerEvents,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var writer = Writer;
        await foreach (var evt in innerEvents.WithCancellation(ct).ConfigureAwait(false))
        {
            if (writer != null)
            {
                if (mapper != null)
                {
                    writer.TryWrite(mapper(evt));
                }
                else if (evt is T typedEvt)
                {
                    writer.TryWrite(typedEvt);
                }
            }

            yield return evt;
        }
    }
}
