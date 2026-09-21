using AgentCore;
using AgentCore.Context;
using AgentCore.LLM.Chat;
using Xunit;

namespace AgentCoreT08;

public class ContextCompactionLayerTests
{
    private sealed class MockContext(List<Message> initial) : IContext
    {
        public List<Message> Messages { get; } = [.. initial];

        public Task<IReadOnlyList<Message>> ReadAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Message>>(Messages);

        public async IAsyncEnumerable<IContentEvent> WriteAsync(
            IAsyncEnumerable<IMessageEvent> events,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var _ in events.WithCancellation(ct)) { }
            yield break;
        }
    }

    [Fact]
    public async Task Test1_MonitorsConversationTokenLengthAgainstConfiguredThreshold()
    {
        // 5 messages with small content (~25 tokens total)
        var mock = new MockContext([
            new(Role.System, [new Text("System prompt")]),
            new(Role.User, [new Text("Message 1")]),
            new(Role.Assistant, [new Text("Message 2")]),
            new(Role.User, [new Text("Message 3")]),
            new(Role.Assistant, [new Text("Message 4")])
        ]);

        // Threshold = 100 tokens -> does not trigger compaction
        var layer = new ContextCompactionLayer(tokenThreshold: 100, preserveLastTurns: 2, inner: mock);
        var read = await layer.ReadAsync();

        Assert.Equal(5, read.Count);
        Assert.Equal(0, layer.CompactionCount);

        // Threshold = 5 tokens -> triggers compaction
        var compactingLayer = new ContextCompactionLayer(tokenThreshold: 5, preserveLastTurns: 2, inner: mock);
        var compactedRead = await compactingLayer.ReadAsync();

        Assert.True(compactingLayer.CompactionCount > 0);
        Assert.True(compactedRead.Count < 5);
    }

    [Fact]
    public async Task Test2_ReplacesOldestTurnsWithConciseSummaryMessage()
    {
        var mock = new MockContext([
            new(Role.System, [new Text("System directions")]),
            new(Role.User, [new Text("Detailed historical conversation turn A")]),
            new(Role.Assistant, [new Text("Detailed historical conversation turn B")]),
            new(Role.User, [new Text("Recent prompt C")]),
            new(Role.Assistant, [new Text("Recent response D")])
        ]);

        var layer = new ContextCompactionLayer(
            tokenThreshold: 10,
            preserveLastTurns: 2,
            summarizer: msgs => Task.FromResult($"Summarized {msgs.Count} turns including historical details."),
            inner: mock);

        var compacted = await layer.ReadAsync();

        // Should have: System prompt, 1 Summary message, 2 Preserved turns = 4 messages (down from 5)
        Assert.Equal(4, compacted.Count);

        var summaryMsg = compacted[1];
        Assert.Equal(Role.User, summaryMsg.Role);
        Assert.Contains("Summarized 2 turns including historical details", summaryMsg.Contents.OfType<Text>().First().Value);
    }

    [Fact]
    public async Task Test3_PreservesSystemPromptAndLastNTurnsVerbatim()
    {
        var sysText = "You are a specialized financial analyst.";
        var lastTurn1 = "What was Q3 revenue?";
        var lastTurn2 = "Q3 revenue was $4.2B.";

        var mock = new MockContext([
            new(Role.System, [new Text(sysText)]),
            new(Role.User, [new Text("Turn 1")]),
            new(Role.Assistant, [new Text("Turn 2")]),
            new(Role.User, [new Text(lastTurn1)]),
            new(Role.Assistant, [new Text(lastTurn2)])
        ]);

        var layer = new ContextCompactionLayer(tokenThreshold: 10, preserveLastTurns: 2, inner: mock);
        var compacted = await layer.ReadAsync();

        // System message preserved verbatim at index 0
        Assert.Equal(Role.System, compacted[0].Role);
        Assert.Equal(sysText, compacted[0].Contents.OfType<Text>().First().Value);

        // Last 2 turns preserved verbatim at end
        Assert.Equal(lastTurn1, compacted[^2].Contents.OfType<Text>().First().Value);
        Assert.Equal(lastTurn2, compacted[^1].Contents.OfType<Text>().First().Value);
    }

    [Fact]
    public async Task Test4_MaintainsCorrectOrderingAndTurnSemanticsAfterCompaction()
    {
        var mock = new MockContext([
            new(Role.System, [new Text("Root System")]),
            new(Role.User, [new Text("Older Turn 1")]),
            new(Role.Assistant, [new Text("Older Turn 2")]),
            new(Role.User, [new Text("Older Turn 3")]),
            new(Role.Assistant, [new Text("Older Turn 4")]),
            new(Role.User, [new Text("Active User Question")]),
            new(Role.Assistant, [new Text("Active Assistant Reply")])
        ]);

        var layer = new ContextCompactionLayer(tokenThreshold: 10, preserveLastTurns: 2, inner: mock);
        var compacted = await layer.ReadAsync();

        // Verify sequence order: System -> Summary -> Active User -> Active Assistant
        Assert.Equal(Role.System, compacted[0].Role);
        Assert.Contains("[Prior conversation summary]", compacted[1].Contents.OfType<Text>().First().Value);
        Assert.Equal("Active User Question", compacted[2].Contents.OfType<Text>().First().Value);
        Assert.Equal("Active Assistant Reply", compacted[3].Contents.OfType<Text>().First().Value);
    }
}
