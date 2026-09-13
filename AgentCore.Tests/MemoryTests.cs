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
        await context.AppendAsync(new[] { system, user, assistant });
        var prepared = await context.GetAsync();

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
            compactor: new Context.Summarizer(mockLlm)
        );

        var system = new Message(Role.System, [new Text("System instructions")]);
        await context.AppendAsync(new[] { system });
        var prompt = await context.GetAsync();
        
        // Add a message with high token usage (95 tokens, exceeding limit of 90) via Message Metadata
        await context.AppendAsync([new Message(Role.Assistant, [new Text("Reply")], metadata: [new TokenUsage(95, 0, 95)])]);

        // Act - GetMessages again, which should trigger compaction immediately due to high TokenUsage
        var finalPrompt = await context.GetAsync();

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
            compactor: new Context.Summarizer(mockLlm)
        );

        var system = new Message(Role.System, [new Text("Be helpful.")]);
        var firstUser = new Message(Role.User, [new Text("Hello")]);
        await context.AppendAsync(new[] { system, firstUser });
        var prompt1 = await context.GetAsync();
        await context.AppendAsync([new Message(Role.Assistant, [new Text("Reply")], metadata: [new TokenUsage(10, 0, 10)])]);

        var secondUser = new Message(Role.User, [new Text(new string('B', 300))]);

        // Act - Add and GetMessages triggering compaction
        await context.AppendAsync(new[] { secondUser });
        var prepared = await context.GetAsync();

        // Assert
        // Should have System instructions + 1 summary message + secondUser
        Assert.True(prepared.Count >= 3);
        Assert.Equal("Be helpful.", prepared[0].Contents[0].ToString());
        Assert.IsAssignableFrom<Text>(prepared[1].Contents[0]);
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
            compactor: null
        );

        var system = new Message(Role.System, [new Text("Be helpful.")]);
        var msg1 = new Message(Role.User, [new Text("First message")]);
        var msg2 = new Message(Role.User, [new Text("Second message")]);

        await context.AppendAsync(new[] { system, msg1, msg2 });
        var prepared = await context.GetAsync();

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
            compactor: new Context.Summarizer(mockLlm)
        );

        var system = new Message(Role.System, [new Text("Be helpful.")]);
        var firstUser = new Message(Role.User, [new Text("Hello")]);
        var assistant = new Message(Role.Assistant, [new Text("Hi")]);
        
        await context.AppendAsync(new[] { system, firstUser, assistant });
        var prompt1 = await context.GetAsync();
        await context.AppendAsync([new Message(Role.Assistant, [new Text("Reply")], metadata: [new TokenUsage(10, 0, 10)])]);

        var secondUser = new Message(Role.User, [new Text(new string('B', 300))]);
        await context.AppendAsync(new[] { secondUser });

        // Act
        var prepared = await context.GetAsync();

        // Assert
        Assert.Equal(3, prepared.Count);
        Assert.Equal("Be helpful.", prepared[0].Contents[0].ToString());
        Assert.IsAssignableFrom<Text>(prepared[1].Contents[0]);
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
        var toolResult = new Message(Role.Tool, [new ToolResult("call_1", [new Text(giantOutput)])]);

        // Act
        await context.AppendAsync([toolResult]);
        var messages = await context.GetAsync();

        // Assert
        Assert.Single(messages);
        var content = messages[0].Contents[0].ToString();
        Assert.Contains("truncated", content);
        Assert.True(content.Length < 5000);
    }

    [Fact]
    public void ContentTruncator_MultiModalTruncationBehavior()
    {
        var estimator = new Context.Tokenizer();
        var truncator = new Context.Truncator(estimator);

        // 1. Text truncation (10 tokens -> ~40 chars)
        var shortText = new Text("Hello");
        Assert.Same(shortText, truncator.Truncate(shortText, 10));

        var longText = new Text(new string('Z', 100));
        var truncatedText = truncator.Truncate(longText, 10);
        Assert.NotSame(longText, truncatedText);
        Assert.Contains("truncated", truncatedText.ToString());

        // 2. ToolResult truncation
        var toolResult = new ToolResult("call_1", [longText]);
        var truncatedResult = truncator.Truncate(toolResult, 10);
        Assert.NotSame(toolResult, truncatedResult);
        Assert.Contains("truncated", truncatedResult.ToString());

        // 3. ToolCall returns itself unchanged
        IContent toolCall = new ToolCall("call_1", "my_tool", new System.Text.Json.Nodes.JsonObject());
        Assert.Same(toolCall, truncator.Truncate(toolCall, 10));

        // 4. Reasoning truncates thought string when over budget
        IContent shortReasoning = new Reasoning("Short thought");
        Assert.Same(shortReasoning, truncator.Truncate(shortReasoning, 10));

        IContent longReasoning = new Reasoning(new string('R', 100));
        var truncatedReasoning = truncator.Truncate(longReasoning, 10);
        Assert.NotSame(longReasoning, truncatedReasoning);
        Assert.Contains("truncated", truncatedReasoning.ToString());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(50)]
    public void Text_Truncate_AlwaysSatisfiesBudgetInvariant(int maxTokens)
    {
        var estimator = new Context.Tokenizer();
        var truncator = new Context.Truncator(estimator);

        var text = new Text(string.Join("\n", Enumerable.Range(1, 200).Select(i => $"Line {i}: some log payload content here")));
        var truncated = (Text)truncator.Truncate(text, maxTokens);

        Assert.True(estimator.Estimate(truncated) <= maxTokens,
            $"Expected Estimate() ({estimator.Estimate(truncated)}) <= maxTokens ({maxTokens})");
    }

    [Fact]
    public void Text_Truncate_PreservesHeadAndTail()
    {
        var estimator = new Context.Tokenizer();
        var truncator = new Context.Truncator(estimator);

        var content = "HEAD_START" + new string('x', 500) + "TAIL_END";
        var text = new Text(content);

        var truncated = (Text)truncator.Truncate(text, 30);
        var str = truncated.ToString();

        Assert.StartsWith("HEAD_START", str);
        Assert.EndsWith("TAIL_END", str);
        Assert.Contains("truncated", str);
        Assert.True(estimator.Estimate(truncated) <= 30);
    }

    [Fact]
    public async Task ChatContext_PrunesHistoricalReasoning_BeforeLastUserMessage()
    {
        // Arrange
        var context = new Context.ChatContext(contextWindow: 10000);

        // Turn 1
        var user1 = new Message(Role.User, [new Text("Question 1")]);
        var assistant1 = new Message(Role.Assistant, [new Reasoning("Thought for Q1"), new Text("Answer 1")]);

        // Turn 2
        var user2 = new Message(Role.User, [new Text("Question 2")]);
        var assistant2 = new Message(Role.Assistant, [new Reasoning("Thought for Q2"), new Text("Answer 2")]);

        await context.AppendAsync([user1, assistant1, user2, assistant2]);

        // Act
        var messages = await context.GetAsync();

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

        await context.AppendAsync([system, userOverflow]);

        // Act
        var messages = await context.GetAsync();

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

