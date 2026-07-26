using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;

namespace CodeSharp;

public sealed class StreamingLLMLayer : LLMLayer
{
    internal ChannelWriter<ILLMOutput>? Writer { get; set; }

    public override async IAsyncEnumerable<ILLMOutput> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var writer = Writer;
        await foreach (var output in Inner.StreamAsync(messages, options, tools, ct).WithCancellation(ct).ConfigureAwait(false))
        {
            writer?.TryWrite(output);
            yield return output;
        }
    }
}
