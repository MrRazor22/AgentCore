using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;

namespace CodeSharp.Layers;

public sealed class StreamingLLMLayer : LLMLayer
{


    internal ChannelWriter<IMessageEvent>? Writer { get; set; }

    public override async IAsyncEnumerable<IMessageEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var writer = Writer;
        await foreach (var output in Inner.StreamAsync(messages, responseSchema, tools, ct).WithCancellation(ct).ConfigureAwait(false))
        {
            writer?.TryWrite(output);
            yield return output;
        }
    }
}
