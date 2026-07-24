using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;

namespace AgentCore.Tests;

public class MemoryTests
{
    [Fact]
    public async Task PrepareConversationAsync_UnderBudget_ReturnsFullHistoryWithoutChanges()
    {
        // Arrange
        var provider = new MockLLMProvider { ContextWindow = 2000 };
        var tokenCounter = new ApproximateTokenCounter();
        var contextService = new ChatContext(tokenCounter, provider.GetCapabilities(), Array.Empty<Tool>(), new Text("Be helpful."));

        await contextService.AddAsync(new Message(Role.User, new Text("Hello")));
        await contextService.AddAsync(new Message(Role.Assistant, new Text("Hi, how are you?")));

        // Act
        await contextService.AddAsync(new Message(Role.User, new Text("Help")));
        var result = contextService.Messages;

        // Assert
        Assert.Equal(4, result.Count);
        Assert.Equal("Be helpful.", result[0].Contents[0].ForLlm());
        Assert.Equal("Hello", result[1].Contents[0].ForLlm());
        Assert.Equal("Hi, how are you?", result[2].Contents[0].ForLlm());
        Assert.Equal("Help", result[3].Contents[0].ForLlm());
    }

    [Fact]
    public async Task PrepareConversationAsync_ExceedsBudget_PrunesOldest()
    {
        // Arrange
        var provider = new MockLLMProvider { ContextWindow = 2068, ReservedTokens = 2048 };
        var tokenCounter = new ApproximateTokenCounter();
        var contextService = new ChatContext(tokenCounter, provider.GetCapabilities(), Array.Empty<Tool>(), new Text("Be concise."), retentionTarget: 0.5);

        var msg1 = new Message(Role.User, new Text("Message 1: This is a very long message that will be pruned."));
        var msg2 = new Message(Role.Assistant, new Text("Message 2: Short."));
        await contextService.AddAsync(msg1);
        await contextService.AddAsync(msg2);

        // Act
        await contextService.AddAsync(new Message(Role.User, new Text("Current short input")));
        var result = contextService.Messages;

        // Assert
        // Verify msg1 is pruned, but msg2 is kept
        Assert.DoesNotContain(result, m => m.Contents.Any(c => c.ForLlm().Contains("Message 1")));
        Assert.Contains(result, m => m.Contents.Any(c => c.ForLlm().Contains("Message 2")));
    }

    [Fact]
    public async Task UpdateHistoryAsync_ExceedsCapacity_PrunesMasterHistory()
    {
        // Arrange
        var provider = new MockLLMProvider { ContextWindow = 2068, ReservedTokens = 2048 };
        var tokenCounter = new ApproximateTokenCounter();
        var contextService = new ChatContext(tokenCounter, provider.GetCapabilities(), Array.Empty<Tool>(), null, retentionTarget: 0.5);

        // Limit is 20 tokens. We add a very long message which exceeds 20 tokens.
        var msg1 = new Message(Role.User, new Text("This is a very long message that easily exceeds the limit of twenty tokens."));
        var msg2 = new Message(Role.Assistant, new Text("Short."));

        await contextService.AddAsync(msg1);
        await contextService.AddAsync(msg2);

        // Act
        await contextService.AddAsync(new Message(Role.User, new Text("Test")));
        var result = contextService.Messages;

        // Assert
        // Verify that the master history has pruned msg1 because it exceeded capacity.
        Assert.DoesNotContain(result, m => m.Contents.Any(c => c.ForLlm().Contains("exceeds the limit")));
        Assert.Contains(result, m => m.Contents.Any(c => c.ForLlm().Contains("Short.")));
    }

    [Fact]
    public async Task PrepareConversationAsync_ExceedsBudget_DoesNotMutateMasterHistory()
    {
        // Arrange
        var provider = new MockLLMProvider { ContextWindow = 2000 };
        var tokenCounter = new ApproximateTokenCounter();
        var contextService = new ChatContext(tokenCounter, provider.GetCapabilities(), Array.Empty<Tool>(), null, retentionTarget: 0.5);

        var msg1 = new Message(Role.User, new Text("Message 1: This is a very long message."));
        var msg2 = new Message(Role.Assistant, new Text("Message 2: Short."));
        await contextService.AddAsync(msg1);
        await contextService.AddAsync(msg2);

        // Act & Assert 1: Prepare with tight budget so msg1 gets pruned in the output
        var providerTight = new MockLLMProvider { ContextWindow = 2068, ReservedTokens = 2048 };
        var contextServiceTight = new ChatContext(tokenCounter, providerTight.GetCapabilities(), Array.Empty<Tool>(), new Text("Be concise."), retentionTarget: 0.5);

        await contextServiceTight.AddAsync(msg1);
        await contextServiceTight.AddAsync(msg2);

        await contextServiceTight.AddAsync(new Message(Role.User, new Text("Current short input")));
        var resultTight = contextServiceTight.Messages;

        Assert.DoesNotContain(resultTight, m => m.Contents.Any(c => c.ForLlm().Contains("Message 1")));

        // Act & Assert 2: Prepare again with normal budget, msg1 should still be present because Prepare was side-effect-free
        var contextServiceNormal = new ChatContext(tokenCounter, provider.GetCapabilities(), Array.Empty<Tool>(), new Text("Be concise."), retentionTarget: 0.5);
        await contextServiceNormal.AddAsync(msg1);
        await contextServiceNormal.AddAsync(msg2);

        await contextServiceNormal.AddAsync(new Message(Role.User, new Text("Current short input")));
        var resultNormal = contextServiceNormal.Messages;

        Assert.Contains(resultNormal, m => m.Contents.Any(c => c.ForLlm().Contains("Message 1")));
    }

    [Fact]
    public async Task PrepareConversationAsync_ZeroBudget_PrunesAllHistory()
    {
        // Arrange
        var provider = new MockLLMProvider { ContextWindow = 2048, ReservedTokens = 2048 }; // budget = 0
        var tokenCounter = new ApproximateTokenCounter();
        var contextService = new ChatContext(tokenCounter, provider.GetCapabilities(), Array.Empty<Tool>(), null, retentionTarget: 0.5);

        var msg1 = new Message(Role.User, new Text("Hello"));
        await contextService.AddAsync(msg1);

        // Act
        await contextService.AddAsync(new Message(Role.User, new Text("Test")));
        var result = contextService.Messages;

        // Assert: History must be completely pruned
        Assert.Single(result); // Only userInput is left
        Assert.Equal("Test", result[0].Contents[0].ForLlm());
    }

    [Fact]
    public async Task RollingWindowMemory_UnderBudget_AccumulatesWithoutConsolidation()
    {
        // Arrange
        var mockLlm = new MockLLMProvider { ContextWindow = 1000 };
        var tokenCounter = new ApproximateTokenCounter();
        var memoryProvider = new ChatContext(tokenCounter, mockLlm.GetCapabilities(), Array.Empty<Tool>(), null, summarizer: mockLlm);

        // Act
        await memoryProvider.AddAsync(new Message(Role.User, new Text("Short content turn")));

        // Assert
        Assert.Equal(0, mockLlm.CallCount); // no LLM calls made
        await memoryProvider.AddAsync(new Message(Role.User, new Text("Query")));
        var prepared = memoryProvider.Messages;
        Assert.All(prepared, msg => Assert.DoesNotContain("Another language model started", string.Join("\n", msg.Contents.Select(c => c.ForLlm()))));
    }

    [Fact]
    public async Task RollingWindowMemory_ExceedsBudget_TriggersConsolidationForEvictedMessages()
    {
        // Arrange
        var mockLlm = new MockLLMProvider { ContextWindow = 600, ReservedTokens = 50 };
        mockLlm.Enqueue(new Text("Consolidated fact sheet result."));
        var tokenCounter = new ApproximateTokenCounter();

        var capabilities = new LLMCapabilities { ContextWindow = 20, ReservedTokens = 5 };
        var memoryProvider = new ChatContext(tokenCounter, capabilities, Array.Empty<Tool>(), null, retentionTarget: 0.1, summarizer: mockLlm);

        // Act
        await memoryProvider.AddAsync(new Message(Role.User, new Text("This is a very long string designed to exceed the budget and force eviction.")));
        await memoryProvider.AddAsync(new Message(Role.User, new Text("Query")));

        // Assert
        Assert.Equal(1, mockLlm.CallCount);
        var prepared = memoryProvider.Messages;
        Assert.Contains(prepared, msg => msg.Contents.Any(c => c.ForLlm().Contains("Consolidated fact sheet result.")));
    }

    [Fact]
    public async Task ClearAsync_PurgesHistoryAndState()
    {
        // Arrange
        var provider = new MockLLMProvider { ContextWindow = 2000 };
        var tokenCounter = new ApproximateTokenCounter();
        var memory = new ChatContext(tokenCounter, provider.GetCapabilities(), Array.Empty<Tool>(), null);

        await memory.AddAsync(new Message(Role.User, new Text("Old Turn")));

        // Act
        await memory.ClearAsync();

        // Assert
        await memory.AddAsync(new Message(Role.User, new Text("New Turn")));
        var result = memory.Messages;
        Assert.Single(result);
        Assert.Equal("New Turn", result[0].Contents[0].ForLlm());
    }

    [Fact]
    public async Task AddRangeAsync_HydratesHistoryWithoutSideEffects()
    {
        // Arrange
        var provider = new MockLLMProvider { ContextWindow = 2000 };
        var tokenCounter = new ApproximateTokenCounter();
        var memory = new ChatContext(tokenCounter, provider.GetCapabilities(), Array.Empty<Tool>(), null);

        var restored = new List<Message>
        {
            new Message(Role.User, new Text("Restored User Message")),
            new Message(Role.Assistant, new Text("Restored Assistant Message"))
        };

        // Act
        await memory.ClearAsync();
        await memory.AddRangeAsync(restored);

        // Assert
        await memory.AddAsync(new Message(Role.User, new Text("Followup")));
        var result = memory.Messages;
        Assert.Equal(3, result.Count);
        Assert.Equal("Restored User Message", result[0].Contents[0].ForLlm());
        Assert.Equal("Restored Assistant Message", result[1].Contents[0].ForLlm());
        Assert.Equal("Followup", result[2].Contents[0].ForLlm());
    }
}
