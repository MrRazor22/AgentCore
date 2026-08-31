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
        var system = new Message(Role.System, [new Text("Be helpful.")]);
        var user = new Message(Role.User, [new Text("Hello")]);
        var assistant = new Message(Role.Assistant, [new Text("Hi, how are you?")]);

        // Act
        await context.AddAsync(new[] { system, user, assistant });
        var prepared = await context.GetMessagesAsync();

        // Assert
        Assert.Equal(3, prepared.Count);
        Assert.Equal("Be helpful.", prepared[0].Contents[0].ForLlm());
        Assert.Equal("Hello", prepared[1].Contents[0].ForLlm());
        Assert.Equal("Hi, how are you?", prepared[2].Contents[0].ForLlm());
    }

    [Fact]
    public async Task ChatContext_AddUpdatesTokenUsage_TriggersCompactionOnNextGetMessages()
    {
        // Arrange
        var mockLlm = new MockLLMProvider { ContextWindow = 1000 };
        mockLlm.Enqueue(new Text("Compacted summary"));

        var context = new ChatContext(
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
        Assert.Contains(finalPrompt, m => m.Contents.Any(c => c.ForLlm().Contains("Compacted summary")));
    }

    [Fact]
    public async Task ChatContext_ExceedsLimit_WithSummarizer_TriggersConsolidationOnGetMessages()
    {
        // Arrange
        var mockLlm = new MockLLMProvider { ContextWindow = 1000 };
        mockLlm.Enqueue(new Text("This is the compacted summary of history."));

        var context = new ChatContext(
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
        Assert.Equal("Be helpful.", prepared[0].Contents[0].ForLlm());
        Assert.IsType<Text>(prepared[1].Contents[0]);
        Assert.Contains("This is the compacted summary of history.", prepared[1].Contents[0].ForLlm());
        Assert.Equal(new string('B', 300), prepared[^1].Contents[0].ForLlm());
    }

    [Fact]
    public async Task ChatContext_WithoutCompactor_PreservesHistoryWithoutCompaction()
    {
        // Arrange
        var context = new ChatContext(
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

        var context = new ChatContext(
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
        Assert.Equal("Be helpful.", prepared[0].Contents[0].ForLlm());
        Assert.IsType<Text>(prepared[1].Contents[0]);
        Assert.Contains("This is the compacted summary of history.", prepared[1].Contents[0].ForLlm());
        Assert.Equal(new string('B', 300), prepared[2].Contents[0].ForLlm());

        Assert.Equal(2, mockLlm.CallCount);
        Assert.Contains(mockLlm.CapturedMessages[0], m => m.Contents.Any(c => c.ForLlm().Contains("Hello")));
        Assert.DoesNotContain(mockLlm.CapturedMessages[1], m => m.Contents.Any(c => c.ForLlm().Contains("Hello")));
    }

    [Fact]
    public async Task ChatContext_OversizedToolResult_IsTruncatedAtIngress()
    {
        // Arrange - contextWindow = 1000, maxSingleMessageTokens = 200 -> ~800 chars
        var context = new ChatContext(contextWindow: 1000, reserveTokens: 100, maxSingleMessageTokens: 200);
        string giantOutput = new string('A', 5000);
        var toolResult = new Message(Role.Tool, [new ToolResult("call_1", new Text(giantOutput))]);

        // Act
        await context.AddAsync([toolResult]);
        var messages = await context.GetMessagesAsync();

        // Assert
        Assert.Single(messages);
        var content = messages[0].Contents[0].ForLlm();
        Assert.Contains("Output truncated", content);
        Assert.True(content.Length < 5000);
    }

    [Fact]
    public async Task ChatContext_ServerUsageBaseline_PreservedAndAugmentedWithToolResults()
    {
        // Arrange
        var context = new ChatContext(contextWindow: 1000, reserveTokens: 100);
        
        // Add assistant message with 500 server tokens
        var assistantMsg = new Message(Role.Assistant, [new Text("Calling tool...")], new MessageMetadata(Usage: new TokenUsage(400, 100)));
        await context.AddAsync([assistantMsg]);

        // Add tool result without server usage
        var toolMsg = new Message(Role.Tool, [new ToolResult("call_1", new Text("Tool output ok"))]);
        await context.AddAsync([toolMsg]);

        // Act
        var messages = await context.GetMessagesAsync();

        // Assert
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public async Task ChatContext_CustomCompaction_IsInvokedOnOverflow()
    {
        // Arrange
        var customCompactor = new CustomTestCompactor();
        var context = new ChatContext(contextWindow: 50, reserveTokens: 10, compactor: customCompactor);

        var system = new Message(Role.System, [new Text("System")]);
        var userOverflow = new Message(Role.User, [new Text(new string('X', 300))]);

        await context.AddAsync([system, userOverflow]);

        // Act
        var messages = await context.GetMessagesAsync();

        // Assert
        Assert.True(customCompactor.WasInvoked);
        Assert.Single(messages);
        Assert.Equal("CustomCompacted", messages[0].Contents[0].ForLlm());
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
