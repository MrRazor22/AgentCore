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

        public IAsyncEnumerable<IMessageEvent> StreamAsync(
            IReadOnlyList<Message> messages,
            JsonSchema? responseSchema = null,
            IReadOnlyList<AgentCore.Tools.ToolDefinition>? tools = null,
            CancellationToken ct = default)
        {
            return StreamCoreAsync(ct);
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
            new ReasoningStart(0),
            new ReasoningDelta(0, "Thinking hard"),
            new ReasoningEnd(0),
            new TextStart(1),
            new TextDelta(1, "Hello "),
            new TextDelta(1, "world!"),
            new TextEnd(1),
            new ToolCallStart(2, "tc-1", "test_tool"),
            new ToolCallEnd(2),
            new MessageEnd("stop", new TokenUsage(10, 20))
        };

        var mockInner = new MockLLM(expectedOutputs);
        var layer = new StreamingLLMLayer<object>();
        var attachMethod = typeof(LLMLayer).GetMethod("Attach", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        attachMethod!.Invoke(layer, new object[] { mockInner });

        var channel = Channel.CreateUnbounded<object>();
        layer.Writer = channel.Writer;

        var messages = new List<Message> { new Message(Role.User, [new Text("Hi")]) };
        var message = new StreamingMessage(layer.StreamAsync(messages));

        var streamedContents = new List<IContent>();
        await foreach (var content in message.ContentsStream())
        {
            streamedContents.Add(content);
        }

        channel.Writer.Complete();
        var channelResults = new List<object>();
        await foreach (var output in channel.Reader.ReadAllAsync())
        {
            channelResults.Add(output);
        }

        Assert.Equal(3, streamedContents.Count);
        Assert.Equal(expectedOutputs.Count, channelResults.Count);

        Assert.Equal("Thinking hard", ((Reasoning)streamedContents[0]).Thought);
        Assert.Equal("Hello world!", ((Text)streamedContents[1]).Value);
        Assert.Equal("tc-1", ((ToolCall)streamedContents[2]).Id);
    }

    [Fact]
    public async Task StreamAsync_WithCustomMapper_ProjectsEventsToCustomType()
    {
        var expectedOutputs = new List<IMessageEvent>
        {
            new TextStart(0),
            new TextDelta(0, "Hello "),
            new TextDelta(0, "World"),
            new TextEnd(0)
        };

        var mockInner = new MockLLM(expectedOutputs);
        var layer = new StreamingLLMLayer<string>(evt => evt is TextDelta td ? td.Text : evt.GetType().Name);
        var attachMethod = typeof(LLMLayer).GetMethod("Attach", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        attachMethod!.Invoke(layer, new object[] { mockInner });

        var channel = Channel.CreateUnbounded<string>();
        layer.Writer = channel.Writer;

        var messages = new List<Message> { new Message(Role.User, [new Text("Hi")]) };
        var message = new StreamingMessage(layer.StreamAsync(messages));

        await foreach (var _ in message.ContentsStream())
        {
        }

        channel.Writer.Complete();
        var results = new List<string>();
        await foreach (var str in channel.Reader.ReadAllAsync())
        {
            results.Add(str);
        }

        Assert.Equal(new[] { "TextStart", "Hello ", "World", "TextEnd" }, results);
    }

    [Fact]
    public async Task StreamAsync_CancellationPropagatesCorrectly()
    {
        var outputs = new List<IMessageEvent> { new TextDelta(0, "hi") };
        var mockInner = new MockLLM(outputs);
        var layer = new StreamingLLMLayer<IMessageEvent>();
        var attachMethod = typeof(LLMLayer).GetMethod("Attach", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        attachMethod!.Invoke(layer, new object[] { mockInner });

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancel

        var messages = new List<Message>();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            var message = new StreamingMessage(layer.StreamAsync(messages, ct: cts.Token));
            await foreach (var unused in message.ContentsStream(cts.Token))
            {
            }
        });
    }
}
