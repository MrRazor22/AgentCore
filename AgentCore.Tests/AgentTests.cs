using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;

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
        mockProvider.Enqueue(new Text("Acknowledged"));

        var memory = new ChatContext(
            contextWindow: 50000
        );
        await memory.StageAsync(new[] { new Message(Role.User, [new Text("Old message")]) });
        var prompt = await memory.PreparePromptAsync();
        await memory.CommitAsync(Array.Empty<Message>());

        var agent = Agent.Create()
            .WithLLM(lf => mockProvider)
            .WithContext(lf => memory)
            .AddLLMLayer(new AgentCore.Layers.LLM.MessageMergingLayer())
            .Build();

        // Act
        var result = await agent.InvokeAsync<string>(new Text("New message"));

        // Assert
        Assert.Equal("Acknowledged", result);

        // Assert that the LLM provider received recalled messages + current user message
        Assert.Single(mockProvider.CapturedMessages);
        var messagesSentToLlm = mockProvider.CapturedMessages[0];

        // Should include coalesced User message containing: "Old message\nNew message"
        Assert.Single(messagesSentToLlm);
        Assert.Equal(Role.User, messagesSentToLlm[0].Role);
        Assert.Equal("Old message\nNew message", messagesSentToLlm[0].Contents[0].ForLlm());
    }

    [Fact]
    public async Task InvokeAsync_RemembersTurnAfterExecution()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new Text("Model reply"));

        var memory = new MockMemoryProvider();
        var agent = Agent.Create()
            .WithLLM(lf => mockProvider)
            .WithContext(lf => memory)
            .Build();

        // Act
        await agent.InvokeAsync<string>(new Text("User input"));

        // Assert
        // Memory should contain: User: User input, Assistant: Model reply
        var messages = memory.Messages;
        Assert.Equal(2, messages.Count);
        Assert.Equal(Role.User, messages[0].Role);
        Assert.Equal("User input", messages[0].Contents[0].ForLlm());

        Assert.Equal(Role.Assistant, messages[1].Role);
        Assert.Equal("Model reply", messages[1].Contents[0].ForLlm());
    }

    [Fact]
    public async Task InvokeAsync_StructuredOutput_ParsesValidJson()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new Text("{\"Name\":\"John Doe\",\"Age\":30}"));

        var agent = Agent.Create()
            .WithLLM(lf => mockProvider)
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
            .WithLLM(lf => mockProvider)
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
            new TextContentDelta("Streaming "),
            new TextContentDelta("reply"),
            new TextContentEnd(),
            new MessageEnd(FinishReason: "stop")
        );

        var agent = Agent.Create()
            .WithLLM(lf => mockProvider)
            .Build();

        var contents = new List<IContent>();
        await foreach (var ev in agent.InvokeStreamingAsync(new Text("Hi")))
        {
            contents.Add(ev);
        }

        var fullText = string.Concat(contents.OfType<Text>().Select(t => t.Value));
        Assert.Equal("Streaming reply", fullText);
    }

    [Fact]
    public async Task InvokeAsync_PrependsSystemInstructionsToHistory()
    {
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new Text("Success"));

        var agent = Agent.Create()
            .WithInstructions("System instruction baseline")
            .WithLLM(lf => mockProvider)
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
