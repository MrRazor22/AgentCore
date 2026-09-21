using System.Diagnostics;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using Xunit;

namespace AgentCoreT10;

public class StreamObservationLayerTests
{
    private sealed class MockStreamingLLM(Func<IAsyncEnumerable<IMessageEvent>> generator) : ILLM
    {
        public IAsyncEnumerable<IMessageEvent> GenerateAsync(
            IReadOnlyList<Message> messages,
            IReadOnlyList<ToolDefinition>? tools = null,
            JsonSchema? responseSchema = null,
            CancellationToken ct = default)
            => generator();
    }

    [Fact]
    public async Task Test1_YieldsTokenDeltasInRealTime()
    {
        var chunks = new[] { "Response ", "from ", "baseline ", "model." };
        async IAsyncEnumerable<IMessageEvent> StreamGenerator()
        {
            yield return new MessageStart(Role.Assistant, Id: "m1");
            for (int i = 0; i < chunks.Length; i++)
            {
                yield return new MessageDelta("m1", Content: new TextDelta(i, chunks[i]));
                await Task.Yield();
            }
            yield return new MessageEnd(Id: "m1");
        }

        var mock = new MockStreamingLLM(StreamGenerator);
        var observer = new StreamObservationLayer(inner: mock);

        var received = new List<IMessageEvent>();
        await foreach (var evt in observer.GenerateAsync([new Message(Role.User, [new Text("Hello streaming")])]))
        {
            received.Add(evt);
        }

        // Downstream received all events
        Assert.Equal(6, received.Count); // start + 4 deltas + end
        Assert.Equal(chunks, observer.TokenDeltas);
    }

    [Fact]
    public async Task Test2_PassesChunksTransparentlyWithoutBuffering()
    {
        var words = new[] { "alpha ", "beta ", "gamma " };
        async IAsyncEnumerable<IMessageEvent> TimedStreamGenerator()
        {
            yield return new MessageStart(Role.Assistant, Id: "m2");
            for (int i = 0; i < words.Length; i++)
            {
                await Task.Delay(30);
                yield return new MessageDelta("m2", Content: new TextDelta(i, words[i]));
            }
            yield return new MessageEnd(Id: "m2");
        }

        var mock = new MockStreamingLLM(TimedStreamGenerator);
        var observer = new StreamObservationLayer(inner: mock);

        var delays = new List<long>();
        var sw = Stopwatch.StartNew();

        await foreach (var evt in observer.GenerateAsync([new Message(Role.User, [new Text("test")])]))
        {
            if (evt is MessageDelta { Content: TextDelta })
            {
                delays.Add(sw.ElapsedMilliseconds);
            }
        }

        Assert.Equal(3, delays.Count);
        Assert.True(delays[1] > delays[0]);
        Assert.True(delays[2] > delays[1]);
    }

    [Fact]
    public async Task Test3_EmitsDistinctEventsForContentToolAndCompletion()
    {
        async IAsyncEnumerable<IMessageEvent> MixedStreamGenerator()
        {
            yield return new MessageStart(Role.Assistant, Id: "m3");
            yield return new MessageDelta("m3", Content: new TextDelta(0, "I will invoke a tool."));
            yield return new MessageDelta("m3", Content: new ToolCall("c1", "calculator", "{\"expr\":\"2+2\"}"));
            yield return new MessageEnd(Id: "m3");
            await Task.Yield();
        }

        var mock = new MockStreamingLLM(MixedStreamGenerator);
        var observer = new StreamObservationLayer(inner: mock);

        await foreach (var _ in observer.GenerateAsync([new Message(Role.User, [new Text("Compute 2+2")])])) { }

        var eventTypes = observer.ObservedEvents.Select(e => e.EventType).ToList();
        Assert.Contains("token_delta", eventTypes);
        Assert.Contains("tool_call_delta", eventTypes);
        Assert.Contains("completion", eventTypes);
        Assert.True(observer.Completed);
    }

    [Fact]
    public async Task Test4_DoesNotDuplicateOrDropStreamItems()
    {
        var tokens = Enumerable.Range(0, 15).Select(i => $"tok_{i} ").ToArray();
        async IAsyncEnumerable<IMessageEvent> LargeStreamGenerator()
        {
            yield return new MessageStart(Role.Assistant, Id: "m4");
            for (int i = 0; i < tokens.Length; i++)
            {
                yield return new MessageDelta("m4", Content: new TextDelta(i, tokens[i]));
                await Task.Yield();
            }
            yield return new MessageEnd(Id: "m4");
        }

        var mock = new MockStreamingLLM(LargeStreamGenerator);
        var observer = new StreamObservationLayer(inner: mock);

        var downstreamReceived = new List<IMessageEvent>();
        await foreach (var evt in observer.GenerateAsync([new Message(Role.User, [new Text("count")])]))
        {
            downstreamReceived.Add(evt);
        }

        // Downstream received 1 start + 15 deltas + 1 end = 17 items
        Assert.Equal(17, downstreamReceived.Count);
        // Observer observed exactly 15 tokens without duplicates or drops
        Assert.Equal(15, observer.TokenDeltas.Count);
        Assert.Equal(string.Concat(tokens), string.Concat(observer.TokenDeltas));
    }
}
