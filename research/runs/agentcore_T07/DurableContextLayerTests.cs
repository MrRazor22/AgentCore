using AgentCore;
using AgentCore.Context;
using AgentCore.LLM.Chat;
using Xunit;

namespace AgentCoreT07;

public class DurableContextLayerTests
{
    [Fact]
    public async Task Test1_AppendsIncomingEventsToWalSynchronously()
    {
        var store = new InMemoryEventStore();
        var inner = new ChatContext();
        var layer = new DurableContextLayer(store, inner);

        // Stream complete message events
        var events = CreateEvents(
            new MessageStart(Role.User, Id: "u1"),
            new MessageDelta("u1", Content: new TextStart(0)),
            new MessageDelta("u1", Content: new TextDelta(0, "Streaming chunk 1")),
            new MessageDelta("u1", Content: new TextDelta(0, "Streaming chunk 2")),
            new MessageDelta("u1", Content: new TextEnd(0)),
            new MessageEnd(Id: "u1")
        );

        // As the events are written, store.AppendWalAsync is called synchronously
        await foreach (var _ in layer.WriteAsync(events)) { }

        // Turn completed: checkpoint was committed and WAL cleared
        var checkpoint = await store.LoadCheckpointAsync();
        Assert.NotEmpty(checkpoint);
        Assert.Contains("Streaming chunk 1Streaming chunk 2", checkpoint[0].Contents.OfType<Text>().First().Value);
    }

    [Fact]
    public async Task Test2_RecoversFullConversationHistoryUponRehydration()
    {
        var store = new InMemoryEventStore();

        // Phase 1: Session 1 creates conversation
        {
            var inner1 = new ChatContext();
            var layer1 = new DurableContextLayer(store, inner1);

            await layer1.WriteAsync(new Message(Role.User, [new Text("Turn 1 user prompt")]));
            await layer1.WriteAsync(new Message(Role.Assistant, [new Text("Turn 1 assistant answer")]));
        }

        // Phase 2: Session 2 rehydrates from same store
        {
            var inner2 = new ChatContext();
            var layer2 = new DurableContextLayer(store, inner2);

            var recoveredHistory = await layer2.ReadAsync();

            Assert.Equal(2, recoveredHistory.Count);
            Assert.Equal("Turn 1 user prompt", recoveredHistory[0].Contents.OfType<Text>().First().Value);
            Assert.Equal("Turn 1 assistant answer", recoveredHistory[1].Contents.OfType<Text>().First().Value);
        }
    }

    [Fact]
    public async Task Test3_ClearsTemporaryWalUncommittedChunksUponSuccessfulCheckpoint()
    {
        var store = new InMemoryEventStore();
        var inner = new ChatContext();
        var layer = new DurableContextLayer(store, inner);

        var events = CreateEvents(
            new MessageStart(Role.User, Id: "m1"),
            new MessageDelta("m1", Content: new TextStart(0)),
            new MessageDelta("m1", Content: new TextDelta(0, "Test payload")),
            new MessageDelta("m1", Content: new TextEnd(0)),
            new MessageEnd(Id: "m1")
        );

        await foreach (var _ in layer.WriteAsync(events)) { }

        // After successful checkpoint, WAL is cleared
        Assert.Equal(0, store.WalCount);

        var checkpoint = await store.LoadCheckpointAsync();
        Assert.Single(checkpoint);
    }

    [Fact]
    public async Task Test4_RecoversGracefullyWithoutHistoryTruncationAfterSimulatedCrash()
    {
        var store = new InMemoryEventStore();

        // Phase 1: Session 1 commits turn 1
        var inner1 = new ChatContext();
        var layer1 = new DurableContextLayer(store, inner1);
        await layer1.WriteAsync(new Message(Role.User, [new Text("Completed turn 1")]));

        // Now simulate mid-turn stream crash: events written to WAL, but process crashes before checkpoint
        await store.AppendWalAsync(new MessageStart(Role.Assistant, Id: "crash1"));
        await store.AppendWalAsync(new MessageDelta("crash1", Content: new TextStart(0)));
        await store.AppendWalAsync(new MessageDelta("crash1", Content: new TextDelta(0, "In-flight text before crash")));
        await store.AppendWalAsync(new MessageDelta("crash1", Content: new TextEnd(0)));
        await store.AppendWalAsync(new MessageEnd(Id: "crash1"));

        // WAL count is 5
        Assert.Equal(5, store.WalCount);

        // Phase 2: New session recovers after crash
        var inner2 = new ChatContext();
        var layer2 = new DurableContextLayer(store, inner2);

        var recovered = await layer2.ReadAsync();

        // Both turn 1 and crashed turn are recovered intact!
        Assert.Equal(2, recovered.Count);
        Assert.Equal("Completed turn 1", recovered[0].Contents.OfType<Text>().First().Value);
        Assert.Equal("In-flight text before crash", recovered[1].Contents.OfType<Text>().First().Value);

        // And WAL is now committed into checkpoint
        Assert.Equal(0, store.WalCount);
    }

    private static async IAsyncEnumerable<IMessageEvent> CreateEvents(params IMessageEvent[] events)
    {
        foreach (var evt in events)
        {
            yield return evt;
            await Task.Yield();
        }
    }
}
