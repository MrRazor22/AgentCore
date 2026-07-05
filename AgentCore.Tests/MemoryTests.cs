using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AgentCore;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tokens;
using AgentCore.Tooling;

namespace AgentCore.Tests;

public class MemoryTests
{
    [Fact]
    public async Task ChatMemory_Recall_NoSummarizationWhenUnderBudget()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        var tokenCounter = new ApproximateTokenCounter();
        var memory = new ChatMemory(tokenCounter, mockProvider, new ChatMemoryOptions
        {
            MinRecentTokens = 500
        });

        var messages = new List<Message>
        {
            new Message(Role.User, new Text("Hello")),
            new Message(Role.Assistant, new Text("Hi there!"))
        };

        await memory.RememberAsync(messages);

        // Act
        var recalled = await memory.RecallAsync(new Message(Role.User, new Text("New Input")), new TokenBudget(2000));

        // Assert
        Assert.Equal(2, recalled.Count);
        Assert.Equal("Hello", recalled[0].Contents[0].ForLlm());
        Assert.Equal("Hi there!", recalled[1].Contents[0].ForLlm());
        Assert.Equal(0, mockProvider.CallCount); // Provider should not be called to summarize
    }

    [Fact]
    public async Task ChatMemory_Recall_SummarizesWhenExceedingBudget()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueAction(() => new[] { new TextDelta("Summarized conversation history.") });

        var tokenCounter = new ApproximateTokenCounter();
        // Set compression settings so summary triggers easily
        var memory = new ChatMemory(tokenCounter, mockProvider, new ChatMemoryOptions
        {
            MinRecentTokens = 10,
            CompressionTarget = 0.5
        });

        // Create messages with large content to trigger compression
        var messages = new List<Message>
        {
            new Message(Role.User, new Text("This is a very long message to push token counts way up")),
            new Message(Role.Assistant, new Text("Yes indeed this is another long sentence containing several tokens"))
        };

        await memory.RememberAsync(messages);

        // Act
        // Recall with a small budget (e.g. 20 tokens)
        var recalled = await memory.RecallAsync(new Message(Role.User, new Text("Hi")), new TokenBudget(20));

        // Assert
        Assert.NotEmpty(recalled);
        Assert.Equal(Role.System, recalled[0].Role);
        Assert.Contains("Conversation Summary", recalled[0].Contents[0].ForLlm());
        Assert.Contains("Summarized conversation history.", recalled[0].Contents[0].ForLlm());
        Assert.Equal(1, mockProvider.CallCount);
    }

    [Fact]
    public async Task ChatMemory_ConcurrentAccess_IsThreadSafe()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        var tokenCounter = new ApproximateTokenCounter();
        var memory = new ChatMemory(tokenCounter, mockProvider);

        // Act & Assert
        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(async () =>
        {
            await memory.RememberAsync(new[] { new Message(Role.User, new Text($"Msg {i}")) });
            await memory.RecallAsync(new Message(Role.User, new Text("")), new TokenBudget(0));
        })).ToArray();

        await Task.WhenAll(tasks);

        var recalled = await memory.RecallAsync(new Message(Role.User, new Text("")), new TokenBudget(0));
        Assert.Equal(50, recalled.Count);
    }

    [Fact]
    public async Task ChatMemory_EmptyMemory_ReturnsEmptyList()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        var tokenCounter = new ApproximateTokenCounter();
        var memory = new ChatMemory(tokenCounter, mockProvider);

        // Act
        var recalled = await memory.RecallAsync(new Message(Role.User, new Text("")), new TokenBudget(0));

        // Assert
        Assert.Empty(recalled);
    }

    [Fact]
    public async Task ChatMemory_BudgetSmallerThanHistory_FallsBackToTrimming()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        var tokenCounter = new ApproximateTokenCounter();
        var memory = new ChatMemory(tokenCounter, mockProvider, new ChatMemoryOptions
        {
            MinRecentTokens = 500, // Large recent tokens requirement prevents summarization of current recent history
            CompressionTarget = 0.8
        });

        await memory.RememberAsync(new[] { new Message(Role.User, new Text("Very early message")) });
        await memory.RememberAsync(new[] { new Message(Role.User, new Text("Later message")) });

        // Act
        // Set low budget of 8 tokens, prompting trimming fallback because summarizableCount is 0 (due to high MinRecentTokens)
        var recalled = await memory.RecallAsync(new Message(Role.User, new Text("Input")), new TokenBudget(8));

        // Assert - The oldest message "Very early message" should be trimmed/removed.
        Assert.Single(recalled);
        Assert.Equal("Later message", recalled[0].Contents[0].ForLlm());
    }

    [Fact]
    public async Task ChatMemory_ClearAsync_ClearsSessionSuccessfully()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        var tokenCounter = new ApproximateTokenCounter();
        var memory = new ChatMemory(tokenCounter, mockProvider);

        await memory.RememberAsync(new[] { new Message(Role.User, new Text("Hello")) });

        // Act
        await memory.ClearAsync();
        var recalled = await memory.RecallAsync(new Message(Role.User, new Text("")), new TokenBudget(0));

        // Assert
        Assert.Empty(recalled);
    }
}
