using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;

namespace AgentCore.Tests;

public class MemoryTests
{
    [Fact]
    public async Task ChatContext_UnderLimit_AccumulatesMessagesAndEstimatesTokens()
    {
        // Arrange
        var context = new ChatContext(contextWindow: 1000, reserveTokens: 100);
        var system = new Message(Role.System, new Text("Be helpful."));
        var user = new Message(Role.User, new Text("Hello"));
        var assistant = new Message(Role.Assistant, new Text("Hi, how are you?"));

        // Act
        await context.StageAsync(new[] { system, user, assistant });
        var prepared = await context.PreparePromptAsync();

        // Assert
        Assert.Equal(3, prepared.Count);
        Assert.Equal("Be helpful.", prepared[0].Contents[0].ForLlm());
        Assert.Equal("Hello", prepared[1].Contents[0].ForLlm());
        Assert.Equal("Hi, how are you?", prepared[2].Contents[0].ForLlm());

        await context.CommitAsync(Array.Empty<Message>(), new TokenUsage(50, 0));

        var postCommitPrepared = await context.PreparePromptAsync();
        Assert.Equal(3, postCommitPrepared.Count);
    }

    [Fact]
    public async Task ChatContext_CommitUpdatesTokenUsage_TriggersCompactionOnNextPrepare()
    {
        // Arrange
        var mockLlm = new MockLLMProvider { ContextWindow = 1000 };
        mockLlm.Enqueue(new Text("Compacted summary"));

        var context = new ChatContext(
            contextWindow: 100, // limit = 90
            reserveTokens: 10,
            summarizer: mockLlm
        );

        var system = new Message(Role.System, new Text("System instructions"));
        await context.StageAsync(new[] { system });
        var prompt = await context.PreparePromptAsync();
        
        // Commit a high token usage (95 tokens, exceeding limit of 90)
        await context.CommitAsync(Array.Empty<Message>(), new TokenUsage(95, 0));

        // Act - Prepare again, which should trigger compaction immediately due to high TokenUsage
        var finalPrompt = await context.PreparePromptAsync();

        // Assert
        Assert.Contains(finalPrompt, m => m.Contents.Any(c => c.ForLlm().Contains("Compacted summary")));
    }

    [Fact]
    public async Task ChatContext_ExceedsLimit_WithSummarizer_TriggersConsolidationOnPrepare()
    {
        // Arrange
        var mockLlm = new MockLLMProvider { ContextWindow = 1000 };
        mockLlm.Enqueue(new Text("This is the compacted summary of history."));

        var context = new ChatContext(
            contextWindow: 25, // very small limit to trigger compaction easily
            reserveTokens: 5,
            summarizer: mockLlm
        );

        var system = new Message(Role.System, new Text("Be helpful."));
        var firstUser = new Message(Role.User, new Text("Hello"));
        await context.StageAsync(new[] { system, firstUser });
        var prompt1 = await context.PreparePromptAsync();
        await context.CommitAsync(Array.Empty<Message>(), new TokenUsage(10, 0));

        var secondUser = new Message(Role.User, new Text(new string('B', 300)));

        // Act - Prepare triggering compaction
        await context.StageAsync(new[] { secondUser });
        var prepared = await context.PreparePromptAsync();

        // Assert
        // Should have System instructions + 1 summary message + secondUser
        Assert.Equal(3, prepared.Count);
        Assert.Equal("Be helpful.", prepared[0].Contents[0].ForLlm());
        Assert.IsType<CompactedSummary>(prepared[1].Contents[0]);
        Assert.Contains("This is the compacted summary of history.", prepared[1].Contents[0].ForLlm());
        Assert.Equal(new string('B', 300), prepared[2].Contents[0].ForLlm());
    }

    [Fact]
    public async Task ChatContext_ExceedsLimit_NoSummarizer_TriggersRollingTrimmingOnPrepare()
    {
        // Arrange
        var context = new ChatContext(
            contextWindow: 30, // very small limit
            reserveTokens: 10,
            summarizer: null // no summarizer
        );

        var system = new Message(Role.System, new Text("Be helpful."));
        var msg1 = new Message(Role.User, new Text("First message"));
        var msg2 = new Message(Role.User, new Text("Second message"));
        var msg3 = new Message(Role.User, new Text("Third message that will definitely cause overflow and force eviction"));

        // Commit first two messages to committed history
        await context.StageAsync(new[] { system, msg1, msg2 });
        var prompt1 = await context.PreparePromptAsync();
        await context.CommitAsync(Array.Empty<Message>(), new TokenUsage(10, 0));

        // Act - Prepare triggering rolling trimming
        await context.StageAsync(new[] { msg3 });
        var prepared = await context.PreparePromptAsync();

        // Assert
        // Oldest message (First message) should be evicted. Only System instructions, second, and third should remain
        Assert.DoesNotContain(prepared, m => m.Contents.Any(c => c.ForLlm().Contains("First message")));
    }

    [Fact]
    public async Task ChatContext_ExceedsLimit_SummarizerThrowsException_RetriesWithReducedMessages()
    {
        // Arrange
        var mockLlm = new MockLLMProvider { ContextWindow = 1000 };
        mockLlm.EnqueueException(new Exception("Context limit exceeded"));
        mockLlm.Enqueue(new Text("This is the compacted summary of history."));

        var context = new ChatContext(
            contextWindow: 30,
            reserveTokens: 5,
            summarizer: mockLlm
        );

        var system = new Message(Role.System, new Text("Be helpful."));
        var firstUser = new Message(Role.User, new Text("Hello"));
        var assistant = new Message(Role.Assistant, new Text("Hi"));
        
        await context.StageAsync(new[] { system, firstUser, assistant });
        var prompt1 = await context.PreparePromptAsync();
        await context.CommitAsync(Array.Empty<Message>(), new TokenUsage(10, 0));

        var secondUser = new Message(Role.User, new Text(new string('B', 300)));
        await context.StageAsync(new[] { secondUser });

        // Act
        var prepared = await context.PreparePromptAsync();

        // Assert
        Assert.Equal(3, prepared.Count);
        Assert.Equal("Be helpful.", prepared[0].Contents[0].ForLlm());
        Assert.IsType<CompactedSummary>(prepared[1].Contents[0]);
        Assert.Contains("This is the compacted summary of history.", prepared[1].Contents[0].ForLlm());
        Assert.Equal(new string('B', 300), prepared[2].Contents[0].ForLlm());

        Assert.Equal(2, mockLlm.CallCount);
        Assert.Contains(mockLlm.CapturedMessages[0], m => m.Contents.Any(c => c.ForLlm().Contains("Hello")));
        Assert.DoesNotContain(mockLlm.CapturedMessages[1], m => m.Contents.Any(c => c.ForLlm().Contains("Hello")));
    }
}
