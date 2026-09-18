using AgentCore.Context;
using AgentCore.Layers.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tool;

namespace AgentCore.Tests;

public class AgentTests
{
    private class TestDto
    {
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    [Fact]
    public async Task InvokeAsync_MergesRecalledHistoryWithCurrentInput()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new TextStart(0), new TextDelta(0, "Acknowledged"), new TextEnd(0), new MessageEnd());

        var memory = new Context.ChatContext(
            contextWindow: 50000
        );
        await memory.PrepareAsync(new[] { new Message(Role.User, [new Text("Old message")]) });

        var agent = Agent.Create()
            .UseLLM(llm => llm.Use(lf => mockProvider))
            .UseContext(ctx => ctx.Use(lf => memory).AddChatGrammar())
            .Build();

        // Act
        var result = await agent.WithResponse<string>(new Text("New message"));

        // Assert
        Assert.Equal("Acknowledged", result);

        // Assert that the LLM provider received recalled messages + current user message
        Assert.Single(mockProvider.CapturedMessages);
        var messagesSentToLlm = mockProvider.CapturedMessages[0];

        // Should include coalesced User message containing: "Old message\nNew message"
        Assert.Single(messagesSentToLlm);
        Assert.Equal(Role.User, messagesSentToLlm[0].Role);
        Assert.Equal("Old message\nNew message", messagesSentToLlm[0].Contents[0].ToString());
    }

    [Fact]
    public async Task InvokeAsync_RemembersTurnAfterExecution()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new TextStart(0), new TextDelta(0, "Model reply"), new TextEnd(0), new MessageEnd());

        var memory = new MockMemoryProvider();
        var agent = Agent.Create()
            .WithLLM(lf => mockProvider)
            .WithContext(lf => memory)
            .Build();

        // Act
        await agent.WithResponse<string>(new Text("User input"));

        // Assert
        var messages = memory.Messages;
        Assert.Equal(2, messages.Count);
        Assert.Equal(Role.User, messages[0].Role);
        Assert.Equal("User input", messages[0].Contents[0].ToString());

        Assert.Equal(Role.Assistant, messages[1].Role);
        Assert.Equal("Model reply", messages[1].Contents[0].ToString());
    }

    [Fact]
    public async Task InvokeAsync_StructuredOutput_ParsesValidJson()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new TextStart(0), new TextDelta(0, "{\"Name\":\"John Doe\",\"Age\":30}"), new TextEnd(0), new MessageEnd());

        var agent = Agent.Create()
            .WithLLM(lf => mockProvider)
            .Build();

        // Act
        var result = await agent.WithResponse<TestDto>(new Text("Get user details"));

        // Assert
        Assert.NotNull(result);
        Assert.Equal("John Doe", result.Name);
        Assert.Equal(30, result.Age);
    }

    [Fact]
    public async Task InvokeStreamingAsync_StreamsEventsToCompletion()
    {
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(
            new TextStart(0),
            new TextDelta(0, "Streaming "),
            new TextDelta(0, "reply"),
            new TextEnd(0),
            new MessageEnd()
        );

        var agent = Agent.Create()
            .WithLLM(lf => mockProvider)
            .Build();

        var events = new List<IContentEvent>();
        await foreach (var ev in agent.InvokeStreamingAsync(new Text("Hi")))
        {
            events.Add(ev);
        }

        var fullText = string.Concat(events.OfType<TextDelta>().Select(t => t.Text));
        Assert.Equal("Streaming reply", fullText);
    }
}
