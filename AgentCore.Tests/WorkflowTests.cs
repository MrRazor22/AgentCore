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
        var context = new MockMemoryProvider();
        var agent = new Agent(context, llm, tooling);
        var input = new Text("Hello");

        // Act
        var events = new List<IAgentEvent>();
        await foreach (var item in agent.InvokeStreamingAsync(input))
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
        var context = new MockMemoryProvider();
        var agent = new Agent(context, llm, tooling);
        var input = new Text("Weather in London?");

        // Act
        var events = new List<IAgentEvent>();
        await foreach (var item in agent.InvokeStreamingAsync(input))
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

    [Fact]
    public async Task ExecuteAsync_CompletedContentEvents_MaterializesAndEmitsWithoutDuplicates()
    {
        var provider = new MockLLMProvider();
        provider.Enqueue(
            new ReasoningStart(0),
            new ReasoningDelta(0, "Thinking hard"),
            new ReasoningEnd(0),
            new TextStart(1),
            new TextDelta(1, "The answer is 42"),
            new TextEnd(1),
            new ToolCallStart(2, "call_calc", "calculator"),
            new ToolCallEnd(2),
            new MessageEnd(FinishReason: "stop")
        );

        var tooling = new MockTooling();
        var (llm, _) = CreateServices(provider, tooling);
        var context = new MockMemoryProvider();
        var agent = new Agent(context, llm, tooling);

        var events = new List<IAgentEvent>();
        await foreach (var evt in agent.InvokeStreamingAsync(new Text("Calculate")))
        {
            events.Add(evt);
        }

        // Verify completed content events are present
        var reasoning = Assert.Single(events.OfType<Reasoning>());
        Assert.Equal("Thinking hard", reasoning.Thought);

        var text = Assert.Single(events.OfType<Text>());
        Assert.Equal("The answer is 42", text.Value);

        var toolCall = Assert.Single(events.OfType<ToolCall>());
        Assert.Equal("call_calc", toolCall.Id);

        // Verify raw streaming events are also preserved
        Assert.Single(events.OfType<ReasoningDelta>());
        Assert.Single(events.OfType<TextDelta>());
    }

    [Fact]
    public async Task ExecuteAsync_OneAssistantMessage_ReceivesMultipleCompletedBlocks()
    {
        var provider = new MockLLMProvider();
        provider.Enqueue(
            new ReasoningStart(0),
            new ReasoningDelta(0, "Step 1"),
            new ReasoningEnd(0),
            new TextStart(1),
            new TextDelta(1, "Step 2"),
            new TextEnd(1),
            new ToolCallStart(2, "call_1", "step3"),
            new ToolCallEnd(2),
            new MessageEnd(FinishReason: "stop")
        );

        var tooling = new MockTooling();
        var (llm, _) = CreateServices(provider, tooling);
        var context = new MockMemoryProvider();
        var agent = new Agent(context, llm, tooling);

        await foreach (var _ in agent.InvokeStreamingAsync(new Text("Start"))) { }

        // Must have: 1 User, 1 Assistant, 1 Tool
        var assistantMessages = context.Messages.Where(m => m.Role == Role.Assistant).ToList();
        Assert.Single(assistantMessages);

        var contents = assistantMessages[0].Contents;
        Assert.Equal(3, contents.Count);
        Assert.IsType<Reasoning>(contents[0]);
        Assert.IsType<Text>(contents[1]);
        Assert.IsType<ToolCall>(contents[2]);
    }

    [Fact]
    public async Task ExecuteAsync_AssistantCommitted_BeforeToolExecutionStarts()
    {
        var provider = new MockLLMProvider();
        provider.Enqueue(
            new ToolCallStart(0, "call_1", "tool_1"),
            new ToolCallEnd(0),
            new MessageEnd(FinishReason: "tool_calls")
        );
        provider.Enqueue(
            new TextStart(0),
            new TextDelta(0, "Done"),
            new TextEnd(0),
            new MessageEnd(FinishReason: "stop")
        );

        var context = new MockMemoryProvider();
        bool assistantWasCommittedWhenToolRan = false;

        var tooling = new MockTooling();
        tooling.Handler = (calls, ct) =>
        {
            assistantWasCommittedWhenToolRan = context.Messages.Any(m => m.Role == Role.Assistant);
            var results = calls.Select(c => new ToolResult(c.Id, [new Text("ok")])).ToList();
            return Task.FromResult<IReadOnlyList<ToolResult>>(results);
        };

        var (llm, _) = CreateServices(provider, tooling);
        var agent = new Agent(context, llm, tooling);

        await foreach (var _ in agent.InvokeStreamingAsync(new Text("Test phasing"))) { }

        Assert.True(assistantWasCommittedWhenToolRan);
    }

    [Fact]
    public async Task ExecuteAsync_ToolResults_PersistedInContext()
    {
        var provider = new MockLLMProvider();
        provider.Enqueue(
            new ToolCallStart(0, "call_p", "persisted_tool"),
            new ToolCallEnd(0),
            new MessageEnd(FinishReason: "stop")
        );

        var tooling = new MockTooling();
        tooling.Handler = (calls, ct) => Task.FromResult<IReadOnlyList<ToolResult>>(
            calls.Select(c => new ToolResult(c.Id, [new Text("Output123")])).ToList()
        );

        var (llm, _) = CreateServices(provider, tooling);
        var context = new MockMemoryProvider();
        var agent = new Agent(context, llm, tooling);

        await foreach (var _ in agent.InvokeStreamingAsync(new Text("Run"))) { }

        var toolMessages = context.Messages.Where(m => m.Role == Role.Tool).ToList();
        Assert.Single(toolMessages);
        var toolResult = Assert.Single(toolMessages[0].Contents.OfType<ToolResult>());
        Assert.Equal("call_p", toolResult.CallId);
        Assert.Equal("Output123", toolResult.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_DoesNotCorruptContext()
    {
        using var cts = new CancellationTokenSource();

        var provider = new MockLLMProvider();
        provider.Enqueue(ct =>
        {
            return StreamGenerator(ct);

            async IAsyncEnumerable<IMessageEvent> StreamGenerator([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
            {
                yield return new MessageStart();
                yield return new TextStart(0);
                yield return new TextDelta(0, "Partial text");
                yield return new TextEnd(0);

                cts.Cancel();
                token.ThrowIfCancellationRequested();
                yield return new MessageEnd(FinishReason: "stop");
            }
        });

        var tooling = new MockTooling();
        var (llm, _) = CreateServices(provider, tooling);
        var context = new MockMemoryProvider();
        var agent = new Agent(context, llm, tooling);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in agent.InvokeStreamingAsync(new Text("Interrupt me"), ct: cts.Token)) { }
        });

        // User message was added, but partial assistant was not committed
        Assert.Single(context.Messages);
        Assert.Equal(Role.User, context.Messages[0].Role);
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateToolCall_Prevented()
    {
        int executionCount = 0;
        var provider = new MockLLMProvider();
        provider.Enqueue(
            new ToolCallStart(0, "call_dup", "my_tool"),
            new ToolCallEnd(0),
            new ToolCallStart(1, "call_dup", "my_tool"), // Duplicate ID in stream
            new ToolCallEnd(1),
            new MessageEnd(FinishReason: "stop")
        );

        var tooling = new MockTooling();
        tooling.Handler = (calls, ct) =>
        {
            Interlocked.Increment(ref executionCount);
            return Task.FromResult<IReadOnlyList<ToolResult>>(
                calls.Select(c => new ToolResult(c.Id, [new Text("ok")])).ToList()
            );
        };

        var (llm, _) = CreateServices(provider, tooling);
        var context = new MockMemoryProvider();
        var agent = new Agent(context, llm, tooling);

        await foreach (var _ in agent.InvokeStreamingAsync(new Text("Run"))) { }

        // Tool was only executed once despite duplicate call ID
        Assert.Equal(1, executionCount);

        // Also test recovery scenario: pre-existing ToolResult in context prevents execution
        var recoveryContext = new MockMemoryProvider();
        await recoveryContext.AppendAsync([
            new Message(Role.Tool, [new ToolResult("call_dup", [new Text("previously executed")])])
        ]);

        provider.Enqueue(
            new ToolCallStart(0, "call_dup", "my_tool"),
            new ToolCallEnd(0),
            new MessageEnd(FinishReason: "stop")
        );

        var recoveryAgent = new Agent(recoveryContext, llm, tooling);
        await foreach (var _ in recoveryAgent.InvokeStreamingAsync(new Text("Recover"))) { }

        // Count should still be 1 (did not execute again)
        Assert.Equal(1, executionCount);
    }
}
