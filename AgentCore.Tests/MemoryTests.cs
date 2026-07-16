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
        var contextService = new ChatContextService(tokenCounter, memoryProvider, provider);

        var history = new List<Message>
        {
            new Message(Role.User, new Text("Hello")),
            new Message(Role.Assistant, new Text("Hi, how are you?"))
        };
        await contextService.UpdateHistoryAsync(history);

        // Act
        var result = await contextService.PrepareConversationAsync(
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
        var provider = new MockLLMProvider { ContextWindow = 2068 };
        var tokenCounter = new ApproximateTokenCounter();
        var memoryProvider = new MockMemoryProvider();
        var contextService = new ChatContextService(tokenCounter, memoryProvider, provider, compressionTarget: 0.5);

        var msg1 = new Message(Role.User, new Text("Message 1: This is a very long message that will be pruned."));
        var msg2 = new Message(Role.Assistant, new Text("Message 2: Short."));
        await contextService.UpdateHistoryAsync(new[] { msg1, msg2 });

        // Act
        var result = await contextService.PrepareConversationAsync(
            instructions: new Text("Be concise."),
            userInput: new Message(Role.User, new Text("Current short input")),
            tools: []
        );

        // Assert
        // Verify msg1 is pruned, but msg2 is kept
        Assert.DoesNotContain(result, m => m.Contents.Any(c => c.ForLlm().Contains("Message 1")));
        Assert.Contains(result, m => m.Contents.Any(c => c.ForLlm().Contains("Message 2")));
    }
}
