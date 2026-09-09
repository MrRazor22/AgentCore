using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;

namespace AgentCore.Tests;

public class WorkflowTests
{
    private (ILLM, ITooling) CreateServices(MockLLMProvider provider, ITooling tooling)
    {
        return (provider, tooling);
    }

    [Fact]
    public async Task ExecuteAsync_SunnyPath_RunsToCompletion()
    {
        // Arrange
        var provider = new MockLLMProvider();
        provider.Enqueue(new TextStart(0), new TextDelta(0, "Today is sunny."), new TextEnd(0), new MessageEnd());

        var (llm, tooling) = CreateServices(provider, new MockTooling());
        var executor = new ReActWorkflow(llm, tooling);
        var context = new MockMemoryProvider();
        var input = new Text("Hello");

        // Act
        var events = new List<IAgentEvent>();
        await foreach (var item in executor.ExecuteAsync(context, input, responseSchema: null))
        {
            events.Add(item);
        }

        // Assert
        var textDelta = Assert.Single(events.OfType<TextDelta>());
        Assert.Equal("Today is sunny.", textDelta.Text);

        // Assert messages were added to context (User and Assistant)
        var messages = context.Messages;
        Assert.Equal(2, messages.Count);
        Assert.Equal(Role.User, messages[0].Role);
        Assert.Equal("Hello", messages[0].Contents[0].ToString());
        Assert.Equal(Role.Assistant, messages[1].Role);
        Assert.Equal("Today is sunny.", messages[1].Contents[0].ToString());
    }

    [Fact]
    public async Task ExecuteAsync_WithToolCalls_ExecutesAndResumes()
    {
        // Arrange
        var provider = new MockLLMProvider();
        provider.Enqueue(
            new ToolCallStart(0, "call_1", "get_weather"),
            new ToolCallDelta(0, "{\"location\": \"London\"}"),
            new ToolCallEnd(0),
            new MessageEnd(FinishReason: "tool_calls")
        );
        provider.Enqueue(
            new TextStart(0),
            new TextDelta(0, "It is sunny in London."),
            new TextEnd(0),
            new MessageEnd(FinishReason: "stop")
        );

        var tooling = new MockTooling();
        tooling.Handler = (calls, ct) =>
        {
            var results = calls.Select(c => new ToolResult(c.Id, [new Text("Rainy")])).ToList();
            return Task.FromResult<IReadOnlyList<ToolResult>>(results);
        };

        var (llm, _) = CreateServices(provider, tooling);
        var executor = new ReActWorkflow(llm, tooling);
        var context = new MockMemoryProvider();
        var input = new Text("Weather in London?");

        // Act
        var events = new List<IAgentEvent>();
        await foreach (var item in executor.ExecuteAsync(context, input, responseSchema: null))
        {
            events.Add(item);
        }

        // Assert
        Assert.Contains(events, e => e is ToolCall tc && tc.Name == "get_weather");
        Assert.Contains(events, e => e is ToolResult tr && tr.ToString() == "Rainy");
        var finalResponse = events.OfType<TextDelta>().Single();
        Assert.Equal("It is sunny in London.", finalResponse.Text);

        // Verify conversation history captured by provider on the second call
        Assert.Equal(2, provider.CapturedMessages.Count);
        var secondCallHistory = provider.CapturedMessages[1];

        // Should contain User message, Assistant message (with tool call), Tool result message
        Assert.Equal(3, secondCallHistory.Count);
        Assert.Equal(Role.User, secondCallHistory[0].Role);
        Assert.Equal(Role.Assistant, secondCallHistory[1].Role);
        Assert.Equal(Role.Tool, secondCallHistory[2].Role);
        Assert.Equal("Rainy", secondCallHistory[2].Contents[0].ToString());
    }
}
