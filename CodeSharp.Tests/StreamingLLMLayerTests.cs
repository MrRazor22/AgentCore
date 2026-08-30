using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using CodeSharp.Layers;
using Xunit;

namespace CodeSharp.Tests;

public class StreamingLLMLayerTests
{
    private class MockLLM : ILLM
    {
        private readonly List<IMessageEvent> _outputs;

        public MockLLM(List<IMessageEvent> outputs)
        {
            _outputs = outputs;
        }

        public Message StreamAsync(
            IReadOnlyList<Message> messages,
            JsonSchema? responseSchema = null,
            IReadOnlyList<AgentCore.Tools.ToolDefinition>? tools = null,
            CancellationToken ct = default)
        {
            return new Message(StreamCoreAsync(ct));
        }

        private async IAsyncEnumerable<IMessageEvent> StreamCoreAsync([EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var output in _outputs)
            {
                if (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }
                yield return output;
                await Task.Yield();
            }
        }
    }

    [Fact]
    public async Task StreamAsync_ForwardsOutputsInOrderToChannelAndStream()
    {
        var expectedOutputs = new List<IMessageEvent>
        {
            new MessageStart(Role.Assistant),
            new ReasoningContentStart(0),
            new ReasoningContentDelta(0, "Thinking hard"),
            new ReasoningContentEnd(0),
            new TextContentStart(1),
            new TextContentDelta(1, "Hello "),
            new TextContentDelta(1, "world!"),
            new TextContentEnd(1),
            new ToolCallContentStart(2, "tc-1", "test_tool"),
            new ToolCallContentEnd(2),
            new MessageEnd("stop", new TokenUsage(10, 20))
        };

        var mockInner = new MockLLM(expectedOutputs);
        var layer = new StreamingLLMLayer();
        var attachMethod = typeof(LLMLayer).GetMethod("Attach", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        attachMethod!.Invoke(layer, new object[] { mockInner });

        var channel = Channel.CreateUnbounded<IContent>();
        layer.Writer = channel.Writer;

        var messages = new List<Message> { new Message(Role.User, [new Text("Hi")]) };
        var message = layer.StreamAsync(messages);

        var streamedContents = new List<IContent>();
        await foreach (var content in message.ContentsStream())
        {
            streamedContents.Add(content);
        }

        channel.Writer.Complete();
        var channelResults = new List<IContent>();
        await foreach (var output in channel.Reader.ReadAllAsync())
        {
            channelResults.Add(output);
        }

        Assert.Equal(3, streamedContents.Count);
        Assert.Equal(3, channelResults.Count);

        Assert.Equal("Thinking hard", ((Reasoning)channelResults[0]).Thought);
        Assert.Equal("Hello world!", ((Text)channelResults[1]).Value);
        Assert.Equal("tc-1", ((ToolCall)channelResults[2]).Id);
    }

    [Fact]
    public async Task StreamAsync_CancellationPropagatesCorrectly()
    {
        var outputs = new List<IMessageEvent> { new TextContentDelta(0, "hi") };
        var mockInner = new MockLLM(outputs);
        var layer = new StreamingLLMLayer();
        var attachMethod = typeof(LLMLayer).GetMethod("Attach", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        attachMethod!.Invoke(layer, new object[] { mockInner });

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancel

        var messages = new List<Message>();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            var message = layer.StreamAsync(messages, ct: cts.Token);
            await foreach (var unused in message.ContentsStream(cts.Token))
            {
            }
        });
    }
}
