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
        private readonly List<ILLMOutput> _outputs;

        public MockLLM(List<ILLMOutput> outputs)
        {
            _outputs = outputs;
        }



        public async IAsyncEnumerable<ILLMOutput> StreamAsync(
            IReadOnlyList<Message> messages,
            JsonSchema? responseSchema = null,
            IReadOnlyList<AgentCore.Tools.ToolDefinition>? tools = null,
            [EnumeratorCancellation] CancellationToken ct = default)
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
        var expectedOutputs = new List<ILLMOutput>
        {
            new ReasoningDelta("Thinking hard"),
            new TextDelta("Hello "),
            new TextDelta("world!"),
            new ToolCallStart("tc-1", "test_tool"),
            new ToolCallEnd("tc-1"),
            new TokenUsage(10, 20, null),
            new FinishReason("stop")
        };

        var mockInner = new MockLLM(expectedOutputs);
        var layer = new StreamingLLMLayer();
        var attachMethod = typeof(LLMLayer).GetMethod("Attach", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        attachMethod!.Invoke(layer, new object[] { mockInner });

        var channel = Channel.CreateUnbounded<ILLMOutput>();
        layer.Writer = channel.Writer;

        var messages = new List<Message> { new Message(Role.User, [new Text("Hi")]) };
        var streamedResults = new List<ILLMOutput>();

        await foreach (var output in layer.StreamAsync(messages))
        {
            streamedResults.Add(output);
        }

        channel.Writer.Complete();
        var channelResults = new List<ILLMOutput>();
        await foreach (var output in channel.Reader.ReadAllAsync())
        {
            channelResults.Add(output);
        }

        Assert.Equal(expectedOutputs.Count, streamedResults.Count);
        Assert.Equal(expectedOutputs.Count, channelResults.Count);

        for (int i = 0; i < expectedOutputs.Count; i++)
        {
            Assert.Same(expectedOutputs[i], streamedResults[i]);
            Assert.Same(expectedOutputs[i], channelResults[i]);
        }

        Assert.Equal("Thinking hard", ((ReasoningDelta)channelResults[0]).Thought);
        Assert.Equal("Hello ", ((TextDelta)channelResults[1]).Text);
        Assert.Equal("world!", ((TextDelta)channelResults[2]).Text);
        Assert.Equal("tc-1", ((ToolCallStart)channelResults[3]).Id);
        Assert.Equal(10, ((TokenUsage)channelResults[5]).InputTokens);
        Assert.Equal("stop", ((FinishReason)channelResults[6]).Value);
    }

    [Fact]
    public async Task StreamAsync_CancellationPropagatesCorrectly()
    {
        var outputs = new List<ILLMOutput> { new TextDelta("hi") };
        var mockInner = new MockLLM(outputs);
        var layer = new StreamingLLMLayer();
        var attachMethod = typeof(LLMLayer).GetMethod("Attach", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        attachMethod!.Invoke(layer, new object[] { mockInner });

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancel

        var messages = new List<Message>();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in layer.StreamAsync(messages, ct: cts.Token))
            {
            }
        });
    }
}
