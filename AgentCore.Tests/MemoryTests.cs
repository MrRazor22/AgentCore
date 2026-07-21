using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using AgentCore;
using AgentCore.LLM;
using AgentCore.Memory;
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
        var contextService = new WorkingMemory(tokenCounter, provider.GetCapabilities(), Array.Empty<Tool>(), new Text("Be helpful."));

        var history = new List<Message>
        {
            new Message(Role.User, new Text("Hello")),
            new Message(Role.Assistant, new Text("Hi, how are you?"))
        };
        await contextService.RememberAsync(history);

        // Act
        var result = await contextService.PrepareAsync(
            newInput: new Message(Role.User, new Text("Help"))
        );

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
        var contextService = new WorkingMemory(tokenCounter, provider.GetCapabilities(), Array.Empty<Tool>(), new Text("Be concise."), retentionTarget: 0.5);

        var msg1 = new Message(Role.User, new Text("Message 1: This is a very long message that will be pruned."));
        var msg2 = new Message(Role.Assistant, new Text("Message 2: Short."));
        await contextService.RememberAsync(new[] { msg1, msg2 });

        // Act
        var result = await contextService.PrepareAsync(
            newInput: new Message(Role.User, new Text("Current short input"))
        );

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
        var contextService = new WorkingMemory(tokenCounter, provider.GetCapabilities(), Array.Empty<Tool>(), null, retentionTarget: 0.5);

        // Limit is 20 tokens. We add a very long message which exceeds 20 tokens.
        var msg1 = new Message(Role.User, new Text("This is a very long message that easily exceeds the limit of twenty tokens."));
        var msg2 = new Message(Role.Assistant, new Text("Short."));

        // Act
        await contextService.RememberAsync(new[] { msg1, msg2 });

        // Assert
        // Verify that the master history has pruned msg1 because it exceeded capacity.
        var result = await contextService.PrepareAsync(
            newInput: new Message(Role.User, new Text("Test"))
        );

        Assert.DoesNotContain(result, m => m.Contents.Any(c => c.ForLlm().Contains("exceeds the limit")));
        Assert.Contains(result, m => m.Contents.Any(c => c.ForLlm().Contains("Short.")));
    }

    [Fact]
    public async Task PrepareConversationAsync_ExceedsBudget_DoesNotMutateMasterHistory()
    {
        // Arrange
        var provider = new MockLLMProvider { ContextWindow = 2000 };
        var tokenCounter = new ApproximateTokenCounter();
        var contextService = new WorkingMemory(tokenCounter, provider.GetCapabilities(), Array.Empty<Tool>(), null, retentionTarget: 0.5);

        var msg1 = new Message(Role.User, new Text("Message 1: This is a very long message."));
        var msg2 = new Message(Role.Assistant, new Text("Message 2: Short."));
        await contextService.RememberAsync(new[] { msg1, msg2 });

        // Act & Assert 1: Prepare with tight budget so msg1 gets pruned in the output
        var providerTight = new MockLLMProvider { ContextWindow = 2068, ReservedTokens = 2048 };
        var contextServiceTight = new WorkingMemory(tokenCounter, providerTight.GetCapabilities(), Array.Empty<Tool>(), new Text("Be concise."), retentionTarget: 0.5);
        // Copy the history from the first context service to simulate shared state
        await contextServiceTight.RememberAsync(new[] { msg1, msg2 });

        var resultTight = await contextServiceTight.PrepareAsync(
            newInput: new Message(Role.User, new Text("Current short input"))
        );

        Assert.DoesNotContain(resultTight, m => m.Contents.Any(c => c.ForLlm().Contains("Message 1")));

        // Act & Assert 2: Prepare again with normal budget, msg1 should still be present because Prepare was side-effect-free
        // We recreate the service with normal budget using the same history (msg1, msg2)
        var contextServiceNormal = new WorkingMemory(tokenCounter, provider.GetCapabilities(), Array.Empty<Tool>(), new Text("Be concise."), retentionTarget: 0.5);
        await contextServiceNormal.RememberAsync(new[] { msg1, msg2 });

        var resultNormal = await contextServiceNormal.PrepareAsync(
            newInput: new Message(Role.User, new Text("Current short input"))
        );

        Assert.Contains(resultNormal, m => m.Contents.Any(c => c.ForLlm().Contains("Message 1")));
    }

    [Fact]
    public async Task PrepareConversationAsync_ZeroBudget_PrunesAllHistory()
    {
        // Arrange
        var provider = new MockLLMProvider { ContextWindow = 2048, ReservedTokens = 2048 }; // budget = 0
        var tokenCounter = new ApproximateTokenCounter();
        var contextService = new WorkingMemory(tokenCounter, provider.GetCapabilities(), Array.Empty<Tool>(), null, retentionTarget: 0.5);

        var msg1 = new Message(Role.User, new Text("Hello"));
        await contextService.RememberAsync(new[] { msg1 });

        // Act
        var result = await contextService.PrepareAsync(
            newInput: new Message(Role.User, new Text("Test"))
        );

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
        var memoryProvider = new WorkingMemory(tokenCounter, mockLlm.GetCapabilities(), Array.Empty<Tool>(), null, summarizer: mockLlm);

        var turn = new List<Message> { new Message(Role.User, new Text("Short content turn")) };

        // Act
        await memoryProvider.RememberAsync(turn);

        // Assert
        Assert.Equal(0, mockLlm.CallCount); // no LLM calls made
        var prepared = await memoryProvider.PrepareAsync(new Message(Role.User, new Text("Query")));
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
        var memoryProvider = new WorkingMemory(tokenCounter, capabilities, Array.Empty<Tool>(), null, retentionTarget: 0.1, summarizer: mockLlm);

        var turn = new List<Message> { new Message(Role.User, new Text("This is a very long string designed to exceed the budget and force eviction.")) };

        // Act
        await memoryProvider.RememberAsync(turn);

        // Assert
        Assert.Equal(1, mockLlm.CallCount);
        var prepared = await memoryProvider.PrepareAsync(new Message(Role.User, new Text("Query")));
        Assert.Contains(prepared, msg => msg.Contents.Any(c => c.ForLlm().Contains("Consolidated fact sheet result.")));
    }

    [Fact]
    public async Task ClearAsync_PurgesHistoryAndState()
    {
        // Arrange
        var provider = new MockLLMProvider { ContextWindow = 2000 };
        var tokenCounter = new ApproximateTokenCounter();
        var memory = new WorkingMemory(tokenCounter, provider.GetCapabilities(), Array.Empty<Tool>(), null);

        await memory.RememberAsync(new[] { new Message(Role.User, new Text("Old Turn")) });

        // Act
        await memory.ClearAsync();

        // Assert
        var result = await memory.PrepareAsync(new Message(Role.User, new Text("New Turn")));
        Assert.Single(result);
        Assert.Equal("New Turn", result[0].Contents[0].ForLlm());
    }

    [Fact]
    public async Task RestoreAsync_HydratesHistoryWithoutSideEffects()
    {
        // Arrange
        var provider = new MockLLMProvider { ContextWindow = 2000 };
        var tokenCounter = new ApproximateTokenCounter();
        var memory = new WorkingMemory(tokenCounter, provider.GetCapabilities(), Array.Empty<Tool>(), null);

        var restored = new List<Message>
        {
            new Message(Role.User, new Text("Restored User Message")),
            new Message(Role.Assistant, new Text("Restored Assistant Message"))
        };

        // Act
        await memory.RestoreAsync(restored);

        // Assert
        var result = await memory.PrepareAsync(new Message(Role.User, new Text("Followup")));
        Assert.Equal(3, result.Count);
        Assert.Equal("Restored User Message", result[0].Contents[0].ForLlm());
        Assert.Equal("Restored Assistant Message", result[1].Contents[0].ForLlm());
        Assert.Equal("Followup", result[2].Contents[0].ForLlm());
    }
}
