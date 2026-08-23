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
            new StreamChunk(new ReasoningChunk("Thinking hard")),
            new StreamChunk(new TextChunk("Hello ")),
            new StreamChunk(new TextChunk("world!")),
            new StreamChunk(new ToolCallChunk("test_tool", "{}"), Id: "tc-1"),
            new TokenUsage(10, 20, null),
            new FinishReason("stop")
        };

        var mockInner = new MockLLM(expectedOutputs);
        var layer = new StreamingLLMLayer();
        var attachMethod = typeof(LLMLayer).GetMethod("Attach", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        attachMethod!.Invoke(layer, new object[] { mockInner });

        var channel = Channel.CreateUnbounded<ILLMOutput>();
        layer.Writer = channel.Writer;

        var messages = new List<Message>();
        var streamResults = new List<ILLMOutput>();

        await foreach (var output in layer.StreamAsync(messages))
        {
            streamResults.Add(output);
        }

        // Complete channel writer to read
        channel.Writer.Complete();

        var channelResults = new List<ILLMOutput>();
        await foreach (var output in channel.Reader.ReadAllAsync())
        {
            channelResults.Add(output);
        }

        // Verify counts
        Assert.Equal(expectedOutputs.Count, streamResults.Count);
        Assert.Equal(expectedOutputs.Count, channelResults.Count);

        // Verify order and types
        for (int i = 0; i < expectedOutputs.Count; i++)
        {
            Assert.Equal(expectedOutputs[i].GetType(), streamResults[i].GetType());
            Assert.Equal(expectedOutputs[i].GetType(), channelResults[i].GetType());
        }

        Assert.Equal("Thinking hard", ((ReasoningChunk)((StreamChunk)channelResults[0]).Content).Thought);
        Assert.Equal("Hello ", ((TextChunk)((StreamChunk)channelResults[1]).Content).Text);
        Assert.Equal("world!", ((TextChunk)((StreamChunk)channelResults[2]).Content).Text);
        Assert.Equal("tc-1", ((StreamChunk)channelResults[3]).Id);
        Assert.Equal(10, ((TokenUsage)channelResults[4]).InputTokens);
        Assert.Equal("stop", ((FinishReason)channelResults[5]).Value);
    }

    [Fact]
    public async Task StreamAsync_CancellationPropagatesCorrectly()
    {
        var outputs = new List<ILLMOutput> { new StreamChunk(new TextChunk("hi")) };
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
