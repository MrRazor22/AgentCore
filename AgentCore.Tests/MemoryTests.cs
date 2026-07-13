using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using AgentCore;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tokens;

namespace AgentCore.Tests;

public class MemoryTests
{
    [Fact]
    public async Task RecallAsync_UnderBudget_ReturnsFullHistoryWithoutChanges()
    {
        // Arrange
        var provider = new MockLLMProvider();
        var tokenCounter = new ApproximateTokenCounter();
        var memory = new ChatMemoryService(tokenCounter, provider);

        var history = new List<Message>
        {
            new Message(Role.User, new Text("Hello")),
            new Message(Role.Assistant, new Text("Hi, how are you?"))
        };
        await memory.RememberAsync(history);

        // Act
        var recalled = await memory.RecallAsync(new Message(Role.User, new Text("Help")), maxTokens: 1000);

        // Assert
        Assert.Equal(2, recalled.Count);
        Assert.Equal("Hello", recalled[0].Contents[0].ForLlm());
        Assert.Equal("Hi, how are you?", recalled[1].Contents[0].ForLlm());
    }

    [Fact]
    public async Task RecallAsync_ExceedsBudget_ButRecentPreservationPreventsCompression_TrimsOldest()
    {
        // Arrange
        var provider = new MockLLMProvider();
        var tokenCounter = new ApproximateTokenCounter();
        
        // Configure MinRecentTokens = 500
        var options = new ChatMemoryOptions { MinRecentTokens = 500 };
        var memory = new ChatMemoryService(tokenCounter, provider, options);

        // Enqueue 2 messages: msg1 (~58 tokens) and msg2 (~35 tokens)
        var msg1 = new Message(Role.User, new Text(new string('A', 250))); // oldest
        var msg2 = new Message(Role.Assistant, new Text(new string('B', 150)));
        await memory.RememberAsync(new[] { msg1, msg2 });

        // Act
        // Invoke with a tight maxTokens limit of 100.
        // Because MinRecentTokens is 500, all messages are within the recent boundary, so no messages can be compressed.
        // Trimming fallback will occur, removing the oldest message (msg1).
        // New total tokens = input (~12) + msg2 (~35) = 47 <= 70 (targetLimit), so the loop terminates immediately keeping msg2.
        var recalled = await memory.RecallAsync(new Message(Role.User, new Text(new string('C', 50))), maxTokens: 100);

        // Assert
        Assert.Single(recalled);
        Assert.Equal(new string('B', 150), recalled[0].Contents[0].ForLlm()); // Oldest 'A' message is trimmed
    }

    [Fact]
    public async Task RecallAsync_ExceedsBudget_TriggersSummarization()
    {
        // Arrange
        var provider = new MockLLMProvider();
        // We enqueue exactly 2 summary responses, which is the exact number of iterations needed.
        provider.Enqueue(
            new TextDelta("Summary One"),
            new MetaDelta(FinishReason.Stop, 10, 5)
        );
        provider.Enqueue(
            new TextDelta("Summary Two"),
            new MetaDelta(FinishReason.Stop, 10, 5)
        );

        var tokenCounter = new ApproximateTokenCounter();
        // Set MinRecentTokens = 10 (low so msg3 remains preserved as recent, while msg1 and msg2 are eligible for compression)
        var options = new ChatMemoryOptions { MinRecentTokens = 10, CompressionTarget = 0.8 };
        var memory = new ChatMemoryService(tokenCounter, provider, options);

        // msg1 (~28 tokens), msg2 (~28 tokens), msg3 (~115 tokens). Total history: 171 tokens.
        var msg1 = new Message(Role.User, new Text(new string('A', 120)));
        var msg2 = new Message(Role.Assistant, new Text(new string('B', 120)));
        var msg3 = new Message(Role.User, new Text(new string('C', 500)));
        await memory.RememberAsync(new[] { msg1, msg2, msg3 });

        // Act
        // Request with budget = 180 tokens. Input is ~12 tokens. Total tokens initially = 183 > 180 (budget).
        // Target limit = 180 * 0.8 = 144 tokens.
        // dynamicChunkSize = 180 / 4 = 45 tokens.
        // Iteration 1: msg1 (~28 tokens) is compressed alone because msg1+msg2 (56 tokens) > 45. Replaced with summary message (~12 tokens).
        // Iteration 2: summary message (~12) + msg2 (~28) = 40 tokens < 45, so they are compressed together. Replaced with summary message (~12 tokens).
        // Final total history = summary (~12) + msg3 (~115) = 127 tokens. Added to input (~12) = 139 tokens <= 144 (targetLimit). Exits!
        var recalled = await memory.RecallAsync(new Message(Role.User, new Text(new string('D', 50))), maxTokens: 180);

        // Assert
        // Verified 2 LLM compression calls occurred
        Assert.Equal(2, provider.CallCount);

        // History contains the final summary message and the preserved recent message (msg3)
        Assert.Equal(2, recalled.Count);
        Assert.Equal(Role.System, recalled[0].Role);
        Assert.Contains("System:\nConversation Summary:\nSummary Two", recalled[0].Contents[0].ForLlm());
        Assert.Equal(Role.User, recalled[1].Role);
        Assert.Equal(new string('C', 500), recalled[1].Contents[0].ForLlm());
    }

    [Fact]
    public async Task RecallAsync_SkipsCompressionWhenNoLimitProvided()
    {
        var provider = new MockLLMProvider();
        var tokenCounter = new ApproximateTokenCounter();
        var memory = new ChatMemoryService(tokenCounter, provider);

        var msg = new Message(Role.User, new Text(new string('A', 5000)));
        await memory.RememberAsync(new[] { msg });

        // maxTokens is null (no budget limit)
        var recalled = await memory.RecallAsync(new Message(Role.User, new Text("Hello")), maxTokens: null);

        // Should return unchanged history, skipping LLM compression checks
        Assert.Single(recalled);
        Assert.Equal(0, provider.CallCount);
    }
}

