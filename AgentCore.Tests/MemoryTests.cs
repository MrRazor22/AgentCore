using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using AgentCore;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.LLM.Chat;

namespace AgentCore.Tests;

public class MemoryTests
{
    [Fact]
    public async Task PrepareConversationAsync_UnderBudget_ReturnsFullHistoryWithoutChanges()
    {
        // Arrange
        var provider = new MockLLMProvider { ContextWindow = 2000 };
        var tokenCounter = new ApproximateTokenCounter();
        var memoryProvider = new MockMemoryProvider();
        var contextService = new ContextService(tokenCounter, memoryProvider, provider);

        var history = new List<Message>
        {
            new Message(Role.User, new Text("Hello")),
            new Message(Role.Assistant, new Text("Hi, how are you?"))
        };
        await contextService.UpdateAsync(history);

        // Act
        var result = await contextService.PrepareAsync(
            instructions: new Text("Be helpful."),
            userInput: new Message(Role.User, new Text("Help")),
            tools: []
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
        var memoryProvider = new MockMemoryProvider();
        var contextService = new ContextService(tokenCounter, memoryProvider, provider, compressionTarget: 0.5);

        var msg1 = new Message(Role.User, new Text("Message 1: This is a very long message that will be pruned."));
        var msg2 = new Message(Role.Assistant, new Text("Message 2: Short."));
        await contextService.UpdateAsync(new[] { msg1, msg2 });

        // Act
        var result = await contextService.PrepareAsync(
            instructions: new Text("Be concise."),
            userInput: new Message(Role.User, new Text("Current short input")),
            tools: []
        );

        // Assert
        // Verify msg1 is pruned, but msg2 is kept
        Assert.DoesNotContain(result, m => m.Contents.Any(c => c.ForLlm().Contains("Message 1")));
        Assert.Contains(result, m => m.Contents.Any(c => c.ForLlm().Contains("Message 2")));
    }

    [Fact]
    public async Task PrepareConversationAsync_WithRecalledContext_WrapsUserTurnWithContextTags()
    {
        // Arrange
        var provider = new MockLLMProvider { ContextWindow = 2000 };
        var tokenCounter = new ApproximateTokenCounter();
        var memoryProvider = new MockMemoryProvider { RecallResult = "User likes C# code patterns." };
        var contextService = new ContextService(tokenCounter, memoryProvider, provider);

        // Act
        var result = await contextService.PrepareAsync(
            instructions: new Text("Be helpful."),
            userInput: new Message(Role.User, new Text("Suggest a clean code tip")),
            tools: []
        );

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("Be helpful.", result[0].Contents[0].ForLlm());
        
        var contents = result[1].Contents;
        Assert.Equal(6, contents.Count);
        Assert.Equal("<retrieved_context>", contents[0].ForLlm());
        Assert.Equal("User likes C# code patterns.", contents[1].ForLlm());
        Assert.Equal("</retrieved_context>", contents[2].ForLlm());
        Assert.Equal("<query>", contents[3].ForLlm());
        Assert.Equal("Suggest a clean code tip", contents[4].ForLlm());
        Assert.Equal("</query>", contents[5].ForLlm());
    }

    [Fact]
    public async Task UpdateHistoryAsync_ExceedsCapacity_PrunesMasterHistory()
    {
        // Arrange
        var provider = new MockLLMProvider { ContextWindow = 2068, ReservedTokens = 2048 };
        var tokenCounter = new ApproximateTokenCounter();
        var memoryProvider = new MockMemoryProvider();
        var contextService = new ContextService(tokenCounter, memoryProvider, provider, compressionTarget: 0.5);

        // Limit is 20 tokens. We add a very long message which exceeds 20 tokens.
        var msg1 = new Message(Role.User, new Text("This is a very long message that easily exceeds the limit of twenty tokens."));
        var msg2 = new Message(Role.Assistant, new Text("Short."));

        // Act
        await contextService.UpdateAsync(new[] { msg1, msg2 });

        // Assert
        // Verify that the master history has pruned msg1 because it exceeded capacity.
        var result = await contextService.PrepareAsync(
            instructions: null,
            userInput: new Message(Role.User, new Text("Test")),
            tools: []
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
        var memoryProvider = new MockMemoryProvider();
        var contextService = new ContextService(tokenCounter, memoryProvider, provider, compressionTarget: 0.5);

        var msg1 = new Message(Role.User, new Text("Message 1: This is a very long message."));
        var msg2 = new Message(Role.Assistant, new Text("Message 2: Short."));
        await contextService.UpdateAsync(new[] { msg1, msg2 });

        // Act & Assert 1: Prepare with tight budget so msg1 gets pruned in the output
        var providerTight = new MockLLMProvider { ContextWindow = 2068, ReservedTokens = 2048 };
        var contextServiceTight = new ContextService(tokenCounter, memoryProvider, providerTight, compressionTarget: 0.5);
        // Copy the history from the first context service to simulate shared state
        await contextServiceTight.UpdateAsync(new[] { msg1, msg2 });

        var resultTight = await contextServiceTight.PrepareAsync(
            instructions: new Text("Be concise."),
            userInput: new Message(Role.User, new Text("Current short input")),
            tools: []
        );

        Assert.DoesNotContain(resultTight, m => m.Contents.Any(c => c.ForLlm().Contains("Message 1")));

        // Act & Assert 2: Prepare again with normal budget, msg1 should still be present because Prepare was side-effect-free
        var resultNormal = await contextServiceTight.PrepareAsync(
            instructions: null,
            userInput: new Message(Role.User, new Text("Current short input")),
            tools: []
        );

        Assert.Contains(resultNormal, m => m.Contents.Any(c => c.ForLlm().Contains("Message 1")));
    }

    [Fact]
    public async Task PrepareConversationAsync_ZeroBudget_PrunesAllHistory()
    {
        // Arrange
        var provider = new MockLLMProvider { ContextWindow = 2048, ReservedTokens = 2048 }; // budget = 0
        var tokenCounter = new ApproximateTokenCounter();
        var memoryProvider = new MockMemoryProvider();
        var contextService = new ContextService(tokenCounter, memoryProvider, provider, compressionTarget: 0.5);

        var msg1 = new Message(Role.User, new Text("Hello"));
        await contextService.UpdateAsync(new[] { msg1 });

        // Act
        var result = await contextService.PrepareAsync(
            instructions: null,
            userInput: new Message(Role.User, new Text("Test")),
            tools: []
        );

        // Assert: History must be completely pruned
        Assert.Single(result); // Only userInput is left
        Assert.Equal("Test", result[0].Contents[0].ForLlm());
    }

    [Fact]
    public async Task MemoryProvider_UnderThreshold_AccumulatesWithoutConsolidation()
    {
        // Arrange
        var mockLlm = new MockLLMProvider { ContextWindow = 1000 };
        var tokenCounter = new ApproximateTokenCounter();
        // Compaction threshold is (1000 - 512) * 0.3 = 146 tokens
        var memoryProvider = new SummarizerMemory(mockLlm, tokenCounter, compactionFraction: 0.3);

        var turn = new List<Message> { new Message(Role.User, new Text("Short content turn")) };

        // Act
        await memoryProvider.RememberAsync(turn);

        // Assert
        Assert.Equal(0, mockLlm.CallCount); // no LLM calls made
        var recall = await memoryProvider.RecallAsync(new Text("any"));
        Assert.Equal(string.Empty, recall.ForLlm());
    }

    [Fact]
    public async Task MemoryProvider_CrossesThreshold_TriggersConsolidation()
    {
        // Arrange
        var mockLlm = new MockLLMProvider { ContextWindow = 600, ReservedTokens = 50 }; 
        mockLlm.Enqueue(new Text("Consolidated fact sheet result."));
        var tokenCounter = new ApproximateTokenCounter();
        
        // Effective context capacity = 600 - 50 = 550
        // Compaction threshold = 550 * 0.02 = 11 tokens
        var memoryProvider = new SummarizerMemory(mockLlm, tokenCounter, compactionFraction: 0.02);

        var turn = new List<Message> { new Message(Role.User, new Text("This is a very long string designed to cross the threshold.")) };

        // Act
        await memoryProvider.RememberAsync(turn);

        // Assert
        Assert.Equal(1, mockLlm.CallCount);
        var recall = await memoryProvider.RecallAsync(new Text("any"));
        Assert.Equal("Consolidated fact sheet result.", recall.ForLlm());
    }

    [Fact]
    public async Task MemoryProvider_OversizedContent_TargetedToolResultTruncation()
    {
        // Arrange
        var mockLlm = new MockLLMProvider { ContextWindow = 600, ReservedTokens = 50 }; 
        mockLlm.Enqueue(new Text("Summary of truncated."));
        var tokenCounter = new ApproximateTokenCounter();
        
        var memoryProvider = new SummarizerMemory(mockLlm, tokenCounter, compactionFraction: 0.1);

        var largeResult = new string('A', 2000);
        var turn = new List<Message> 
        { 
            new Message(Role.User, new Text("Query")),
            new Message(Role.Assistant, new ToolCall("1", "run", new System.Text.Json.Nodes.JsonObject())),
            new Message(Role.Tool, new ToolResult("1", new Text(largeResult)))
        };

        // Act
        await memoryProvider.RememberAsync(turn);

        // Assert
        Assert.Equal(1, mockLlm.CallCount);
        var captured = mockLlm.CapturedMessages.Last();
        // The new turns block should contain the truncated marker
        var turnsContent = captured.Last().Contents.Last().ForLlm();
        Assert.Contains("[truncated due to context limits]", turnsContent);
    }
}
