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
        var context = new Context.ChatContext(contextWindow: 1000, reserveTokens: 100);
        var system = new Message(Role.System, [new Text("Be helpful.")]);
        var user = new Message(Role.User, [new Text("Hello")]);
        var assistant = new Message(Role.Assistant, [new Text("Hi, how are you?")]);

        // Act
        await context.AddAsync(new[] { system, user, assistant });
        var prepared = await context.GetMessagesAsync();

        // Assert
        Assert.Equal(3, prepared.Count);
        Assert.Equal("Be helpful.", prepared[0].Contents[0].ToString());
        Assert.Equal("Hello", prepared[1].Contents[0].ToString());
        Assert.Equal("Hi, how are you?", prepared[2].Contents[0].ToString());
    }

    [Fact]
    public async Task ChatContext_AddUpdatesTokenUsage_TriggersCompactionOnNextGetMessages()
    {
        // Arrange
        var mockLlm = new MockLLMProvider { ContextWindow = 1000 };
        mockLlm.Enqueue(new Text("Compacted summary"));

        var context = new Context.ChatContext(
            contextWindow: 100, // limit = 90
            reserveTokens: 10,
            summarizer: mockLlm
        );

        var system = new Message(Role.System, [new Text("System instructions")]);
        await context.AddAsync(new[] { system });
        var prompt = await context.GetMessagesAsync();
        
        // Add a message with high token usage (95 tokens, exceeding limit of 90) via Message Metadata
        await context.AddAsync([new Message(Role.Assistant, [new Text("Reply")], new MessageMetadata(Usage: new TokenUsage(95, 0)))]);

        // Act - GetMessages again, which should trigger compaction immediately due to high TokenUsage
        var finalPrompt = await context.GetMessagesAsync();

        // Assert
        Assert.Contains(finalPrompt, m => m.Contents.Any(c => c.ToString()?.Contains("Compacted summary") == true));
    }

    [Fact]
    public async Task ChatContext_ExceedsLimit_WithSummarizer_TriggersConsolidationOnGetMessages()
    {
        // Arrange
        var mockLlm = new MockLLMProvider { ContextWindow = 1000 };
        mockLlm.Enqueue(new Text("This is the compacted summary of history."));

        var context = new Context.ChatContext(
            contextWindow: 25, // very small limit to trigger compaction easily
            reserveTokens: 5,
            summarizer: mockLlm
        );

        var system = new Message(Role.System, [new Text("Be helpful.")]);
        var firstUser = new Message(Role.User, [new Text("Hello")]);
        await context.AddAsync(new[] { system, firstUser });
        var prompt1 = await context.GetMessagesAsync();
        await context.AddAsync([new Message(Role.Assistant, [new Text("Reply")], new MessageMetadata(Usage: new TokenUsage(10, 0)))]);

        var secondUser = new Message(Role.User, [new Text(new string('B', 300))]);

        // Act - Add and GetMessages triggering compaction
        await context.AddAsync(new[] { secondUser });
        var prepared = await context.GetMessagesAsync();

        // Assert
        // Should have System instructions + 1 summary message + secondUser
        Assert.True(prepared.Count >= 3);
        Assert.Equal("Be helpful.", prepared[0].Contents[0].ToString());
        Assert.IsType<Text>(prepared[1].Contents[0]);
        Assert.Contains("This is the compacted summary of history.", prepared[1].Contents[0].ToString());
        Assert.Equal(new string('B', 300), prepared[^1].Contents[0].ToString());
    }

    [Fact]
    public async Task ChatContext_WithoutCompactor_PreservesHistoryWithoutCompaction()
    {
        // Arrange
        var context = new Context.ChatContext(
            contextWindow: 30,
            reserveTokens: 10,
            compactor: null,
            summarizer: null
        );

        var system = new Message(Role.System, [new Text("Be helpful.")]);
        var msg1 = new Message(Role.User, [new Text("First message")]);
        var msg2 = new Message(Role.User, [new Text("Second message")]);

        await context.AddAsync(new[] { system, msg1, msg2 });
        var prepared = await context.GetMessagesAsync();

        // Assert
        Assert.Equal(3, prepared.Count);
    }

    [Fact]
    public async Task ChatContext_ExceedsLimit_SummarizerThrowsException_RetriesWithReducedMessages()
    {
        // Arrange
        var mockLlm = new MockLLMProvider { ContextWindow = 1000 };
        mockLlm.EnqueueException(new Exception("Context limit exceeded"));
        mockLlm.Enqueue(new Text("This is the compacted summary of history."));

        var context = new Context.ChatContext(
            contextWindow: 30,
            reserveTokens: 5,
            summarizer: mockLlm
        );

        var system = new Message(Role.System, [new Text("Be helpful.")]);
        var firstUser = new Message(Role.User, [new Text("Hello")]);
        var assistant = new Message(Role.Assistant, [new Text("Hi")]);
        
        await context.AddAsync(new[] { system, firstUser, assistant });
        var prompt1 = await context.GetMessagesAsync();
        await context.AddAsync([new Message(Role.Assistant, [new Text("Reply")], new MessageMetadata(Usage: new TokenUsage(10, 0)))]);

        var secondUser = new Message(Role.User, [new Text(new string('B', 300))]);
        await context.AddAsync(new[] { secondUser });

        // Act
        var prepared = await context.GetMessagesAsync();

        // Assert
        Assert.Equal(3, prepared.Count);
        Assert.Equal("Be helpful.", prepared[0].Contents[0].ToString());
        Assert.IsType<Text>(prepared[1].Contents[0]);
        Assert.Contains("This is the compacted summary of history.", prepared[1].Contents[0].ToString());
        Assert.Equal(new string('B', 300), prepared[2].Contents[0].ToString());

        Assert.Equal(2, mockLlm.CallCount);
        Assert.Contains(mockLlm.CapturedMessages[0], m => m.Contents.Any(c => c.ToString()?.Contains("Hello") == true));
        Assert.DoesNotContain(mockLlm.CapturedMessages[1], m => m.Contents.Any(c => c.ToString()?.Contains("Hello") == true));
    }

    [Fact]
    public async Task ChatContext_OversizedToolResult_IsTruncatedAtIngress()
    {
        // Arrange - contextWindow = 1000, maxSingleMessageTokens = 200 -> ~800 chars
        var context = new Context.ChatContext(contextWindow: 1000, reserveTokens: 100, maxSingleMessageTokens: 200);
        string giantOutput = new string('A', 5000);
        var toolResult = new Message(Role.Tool, [new ToolResult("call_1", new Text(giantOutput))]);

        // Act
        await context.AddAsync([toolResult]);
        var messages = await context.GetMessagesAsync();

        // Assert
        Assert.Single(messages);
        var content = messages[0].Contents[0].ToString();
        Assert.Contains("truncated", content);
        Assert.True(content.Length < 5000);
    }

    [Fact]
    public void IContent_PolymorphicTruncationBehavior()
    {
        // 1. Text truncation (10 tokens -> ~40 chars)
        var shortText = new Text("Hello");
        Assert.Same(shortText, shortText.Truncate(10));

        var longText = new Text(new string('Z', 100));
        var truncatedText = longText.Truncate(10);
        Assert.NotSame(longText, truncatedText);
        Assert.Contains("Content truncated from 100 to 40 characters", truncatedText.ToString());

        // 2. ToolResult truncation
        var toolResult = new ToolResult("call_1", longText);
        var truncatedResult = toolResult.Truncate(10);
        Assert.NotSame(toolResult, truncatedResult);
        Assert.Contains("Content truncated from 100 to 40 characters", truncatedResult.ToString());

        // 3. ToolCall returns itself unchanged
        IContent toolCall = new ToolCall("call_1", "my_tool", new System.Text.Json.Nodes.JsonObject());
        Assert.Same(toolCall, toolCall.Truncate(10));

        // 4. Reasoning truncates thought string when over budget
        IContent shortReasoning = new Reasoning("Short thought");
        Assert.Same(shortReasoning, shortReasoning.Truncate(10));

        IContent longReasoning = new Reasoning(new string('R', 100));
        var truncatedReasoning = longReasoning.Truncate(10);
        Assert.NotSame(longReasoning, truncatedReasoning);
        Assert.Contains("Reasoning truncated from 100 to 40 characters", truncatedReasoning.ToString());
    }

    [Fact]
    public async Task ChatContext_PrunesHistoricalReasoning_BeforeLastUserMessage()
    {
        // Arrange
        var context = new Context.ChatContext(contextWindow: 10000);

        // Turn 1
        var user1 = new Message(Role.User, new Text("Question 1"));
        var assistant1 = new Message(Role.Assistant, [new Reasoning("Thought for Q1"), new Text("Answer 1")]);

        // Turn 2
        var user2 = new Message(Role.User, new Text("Question 2"));
        var assistant2 = new Message(Role.Assistant, [new Reasoning("Thought for Q2"), new Text("Answer 2")]);

        await context.AddAsync([user1, assistant1, user2, assistant2]);

        // Act
        var messages = await context.GetMessagesAsync();

        // Assert: Turn 1 assistant message should have Reasoning pruned, Turn 2 assistant reasoning should be preserved
        Assert.Equal(4, messages.Count);
        Assert.Single(messages[1].Contents); // only Text("Answer 1")
        Assert.IsType<Text>(messages[1].Contents[0]);

        Assert.Equal(2, messages[3].Contents.Count); // Reasoning + Text preserved in current turn
        Assert.IsType<Reasoning>(messages[3].Contents[0]);
        Assert.IsType<Text>(messages[3].Contents[1]);
    }

    [Fact]
    public async Task ChatContext_CustomCompaction_IsInvokedOnOverflow()
    {
        // Arrange
        var customCompactor = new CustomTestCompactor();
        var context = new Context.ChatContext(contextWindow: 50, reserveTokens: 10, compactor: customCompactor);

        var system = new Message(Role.System, [new Text("System")]);
        var userOverflow = new Message(Role.User, [new Text(new string('X', 300))]);

        await context.AddAsync([system, userOverflow]);

        // Act
        var messages = await context.GetMessagesAsync();

        // Assert
        Assert.True(customCompactor.WasInvoked);
        Assert.Single(messages);
        Assert.Equal("CustomCompacted", messages[0].Contents[0].ToString());
    }

    private class CustomTestCompactor : ICompactor
    {
        public bool WasInvoked { get; private set; }
        public Task<IReadOnlyList<Message>> CompactAsync(IReadOnlyList<Message> messages, int tokenLimit, CancellationToken ct = default)
        {
            WasInvoked = true;
            return Task.FromResult<IReadOnlyList<Message>>(new[] { new Message(Role.System, [new Text("CustomCompacted")]) });
        }
    }
}

