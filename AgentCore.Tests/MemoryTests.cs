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
        var prepared = await context.BuildPromptAsync(new[] { system, user, assistant });

        // Assert
        Assert.Equal(3, prepared.Count);
        Assert.Equal("Be helpful.", prepared[0].Contents[0].ForLlm());
        Assert.Equal("Hello", prepared[1].Contents[0].ForLlm());
        Assert.Equal("Hi, how are you?", prepared[2].Contents[0].ForLlm());

        await context.CommitAsync(new TokenUsage(50, 0), Array.Empty<Message>());

        var result = context.Messages;
        Assert.Equal(3, result.Count);
        Assert.True(context.TokenUsage > 0);
    }

    [Fact]
    public async Task ChatContext_UpdateTokenUsage_CalibratesCharsPerTokenAndLocksActuals()
    {
        // Arrange
        var context = new ChatContext(contextWindow: 1000, reserveTokens: 100);
        var system = new Message(Role.System, new Text("System instructions"));
        var user = new Message(Role.User, new Text("User message content here"));

        // Act
        var prompt = await context.BuildPromptAsync(new[] { system, user });
        await context.CommitAsync(new TokenUsage(20, 0), Array.Empty<Message>());

        // Assert
        Assert.Equal(20, context.TokenUsage);
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
        var prompt1 = await context.BuildPromptAsync(new[] { system, firstUser });
        await context.CommitAsync(new TokenUsage(10, 0), Array.Empty<Message>());

        var secondUser = new Message(Role.User, new Text(new string('B', 300)));

        // Act - Prepare triggering compaction
        var prepared = await context.BuildPromptAsync(new[] { secondUser });

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
        var prompt1 = await context.BuildPromptAsync(new[] { system, msg1, msg2 });
        await context.CommitAsync(new TokenUsage(10, 0), Array.Empty<Message>());

        // Act - Prepare triggering rolling trimming
        var prepared = await context.BuildPromptAsync(new[] { msg3 });

        // Assert
        // Oldest message (First message) should be evicted. Only System instructions, second, and third should remain
        Assert.DoesNotContain(prepared, m => m.Contents.Any(c => c.ForLlm().Contains("First message")));
    }
}
