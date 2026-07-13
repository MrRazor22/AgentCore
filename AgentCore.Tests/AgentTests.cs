using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using AgentCore;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;

namespace AgentCore.Tests;

public class AgentTests
{
    private class TestDto
    {
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    [Fact]
    public async Task InvokeAsync_RecallsMemoryBeforeExecution()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new TextDelta("Acknowledged"));

        var memory = new MockMemory();
        memory.History.Add(new Message(Role.User, new Text("Old message")));

        var agent = Agent.Create()
            .WithProvider(mockProvider)
            .UseMemory(memory)
            .Build();

        // Act
        var result = await agent.InvokeAsync<string>(new Text("New message"));

        // Assert
        Assert.Equal("Acknowledged", result);
        
        // Assert that the LLM provider received recalled messages + current user message
        Assert.Single(mockProvider.CapturedMessages);
        var messagesSentToLlm = mockProvider.CapturedMessages[0];
        
        // Should include: User: Old message, User: New message
        Assert.Equal(2, messagesSentToLlm.Count);
        Assert.Equal(Role.User, messagesSentToLlm[0].Role);
        Assert.Equal("Old message", messagesSentToLlm[0].Contents[0].ForLlm());
        Assert.Equal(Role.User, messagesSentToLlm[1].Role);
        Assert.Equal("New message", messagesSentToLlm[1].Contents[0].ForLlm());
    }

    [Fact]
    public async Task InvokeAsync_RemembersTurnAfterExecution()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new TextDelta("Model reply"));

        var memory = new MockMemory();
        var agent = Agent.Create()
            .WithProvider(mockProvider)
            .UseMemory(memory)
            .Build();

        // Act
        await agent.InvokeAsync<string>(new Text("User input"));

        // Assert
        // Memory should contain: User: User input, Assistant: Model reply
        Assert.Equal(2, memory.History.Count);
        Assert.Equal(Role.User, memory.History[0].Role);
        Assert.Equal("User input", memory.History[0].Contents[0].ForLlm());
        
        Assert.Equal(Role.Assistant, memory.History[1].Role);
        Assert.Equal("Model reply", memory.History[1].Contents[0].ForLlm());
    }

    [Fact]
    public async Task InvokeAsync_StructuredOutput_ParsesValidJson()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new TextDelta("{\"Name\":\"John Doe\",\"Age\":30}"));

        var agent = Agent.Create()
            .WithProvider(mockProvider)
            .Build();

        // Act
        var result = await agent.InvokeAsync<TestDto>(new Text("Get user details"));

        // Assert
        Assert.NotNull(result);
        Assert.Equal("John Doe", result.Name);
        Assert.Equal(30, result.Age);
    }

    [Fact]
    public async Task InvokeAsync_ExceptionPropagates()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueException(new InvalidOperationException("Fatal provider error"));

        var agent = Agent.Create()
            .WithProvider(mockProvider)
            .Build();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await agent.InvokeAsync<string>(new Text("Hello"));
        });
        Assert.Equal("Fatal provider error", ex.Message);
    }

    [Fact]
    public async Task InvokeStreamingAsync_StreamsEventsToCompletion()
    {
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(
            new TextDelta("Streaming "),
            new TextDelta("reply"),
            new MetaDelta(FinishReason.Stop, 10, 5)
        );

        var agent = Agent.Create()
            .WithProvider(mockProvider)
            .Build();

        var events = new List<AgentEvent>();
        await foreach (var ev in agent.InvokeStreamingAsync(new Text("Hi")))
        {
            events.Add(ev);
        }

        var textEvents = events.OfType<TextEvent>().ToList();
        Assert.Equal(2, textEvents.Count);
        Assert.Equal("Streaming ", textEvents[0].Delta);
        Assert.Equal("reply", textEvents[1].Delta);

        var responseEvent = events.OfType<AgentResponseEvent>().Single();
        Assert.Equal("Streaming reply", responseEvent.Response);
    }

    [Fact]
    public async Task InvokeAsync_PrependsSystemInstructionsToHistory()
    {
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new TextDelta("Success"));

        var agent = Agent.Create()
            .WithInstructions("System instruction baseline")
            .WithProvider(mockProvider)
            .Build();

        await agent.InvokeAsync<string>(new Text("User baseline"));

        Assert.Single(mockProvider.CapturedMessages);
        var messages = mockProvider.CapturedMessages[0];
        Assert.Equal(2, messages.Count);
        Assert.Equal(Role.System, messages[0].Role);
        Assert.Equal("System instruction baseline", messages[0].Contents[0].ForLlm());
        Assert.Equal(Role.User, messages[1].Role);
        Assert.Equal("User baseline", messages[1].Contents[0].ForLlm());
    }
}
