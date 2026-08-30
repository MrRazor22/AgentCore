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


    internal ChannelWriter<IContent>? Writer { get; set; }

    public override Message StreamAsync(
        IReadOnlyList<Message> messages,
        JsonSchema? responseSchema = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        CancellationToken ct = default)
    {
        var inner = Inner.StreamAsync(messages, responseSchema, tools, ct);
        return new Message(InterceptContentsAsync(inner, ct));
    }

    private async IAsyncEnumerable<IContent> InterceptContentsAsync(
        Message inner,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var writer = Writer;
        await foreach (var content in inner.ContentsStream(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            writer?.TryWrite(content);
            yield return content;
        }
    }
}
