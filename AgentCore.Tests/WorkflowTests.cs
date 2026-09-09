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
        var executor = new ReActWorkflow(llm, tooling);
        var context = new MockMemoryProvider();

        var events = new List<IAgentEvent>();
        await foreach (var evt in executor.ExecuteAsync(context, new Text("Calculate"), null))
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
        var executor = new ReActWorkflow(llm, tooling);
        var context = new MockMemoryProvider();

        await foreach (var _ in executor.ExecuteAsync(context, new Text("Start"), null)) { }

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
    public async Task ExecuteAsync_ToolExecutionStarts_BeforeRemainingStreamFinishes()
    {
        var toolStartedTcs = new TaskCompletionSource<bool>();
        var allowStreamToFinishTcs = new TaskCompletionSource<bool>();

        var provider = new MockLLMProvider();
        provider.Enqueue(ct =>
        {
            return StreamGenerator(ct);

            async IAsyncEnumerable<IMessageEvent> StreamGenerator([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
            {
                yield return new MessageStart();
                yield return new ToolCallStart(0, "call_early", "early_tool");
                yield return new ToolCallEnd(0);

                // Wait until the tool has actually started execution before emitting the rest of the stream
                await toolStartedTcs.Task;

                yield return new TextStart(1);
                yield return new TextDelta(1, "LLM finished after tool started");
                yield return new TextEnd(1);
                yield return new MessageEnd(FinishReason: "stop");
            }
        });

        var tooling = new MockTooling();
        tooling.Handler = (calls, ct) =>
        {
            toolStartedTcs.TrySetResult(true);
            var results = calls.Select(c => new ToolResult(c.Id, [new Text("Done")])).ToList();
            return Task.FromResult<IReadOnlyList<ToolResult>>(results);
        };

        var (llm, _) = CreateServices(provider, tooling);
        var executor = new ReActWorkflow(llm, tooling);
        var context = new MockMemoryProvider();

        var events = new List<IAgentEvent>();
        await foreach (var evt in executor.ExecuteAsync(context, new Text("Test concurrency"), null))
        {
            events.Add(evt);
        }

        Assert.True(toolStartedTcs.Task.IsCompletedSuccessfully);
        Assert.Contains(events, e => e is ToolResult tr && tr.CallId == "call_early");
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
        var executor = new ReActWorkflow(llm, tooling);
        var context = new MockMemoryProvider();

        await foreach (var _ in executor.ExecuteAsync(context, new Text("Run"), null)) { }

        var toolMessages = context.Messages.Where(m => m.Role == Role.Tool).ToList();
        Assert.Single(toolMessages);
        var toolResult = Assert.Single(toolMessages[0].Contents.OfType<ToolResult>());
        Assert.Equal("call_p", toolResult.CallId);
        Assert.Equal("Output123", toolResult.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_RetainsAlreadyPersistedContent()
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
                yield return new TextDelta(0, "Persisted text");
                yield return new TextEnd(0);
                yield return new ToolCallStart(1, "call_saved", "saved_tool");
                yield return new ToolCallEnd(1);

                // Cancel the operation during streaming
                cts.Cancel();
                token.ThrowIfCancellationRequested();
                yield return new MessageEnd(FinishReason: "stop");
            }
        });

        var tooling = new MockTooling();
        var (llm, _) = CreateServices(provider, tooling);
        var executor = new ReActWorkflow(llm, tooling);
        var context = new MockMemoryProvider();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in executor.ExecuteAsync(context, new Text("Interrupt me"), null, cts.Token)) { }
        });

        // Verify already-persisted contents are retained in context
        var assistantMsg = Assert.Single(context.Messages, m => m.Role == Role.Assistant);
        Assert.Equal(2, assistantMsg.Contents.Count);
        Assert.Equal("Persisted text", Assert.IsType<Text>(assistantMsg.Contents[0]).Value);
        Assert.Equal("call_saved", Assert.IsType<ToolCall>(assistantMsg.Contents[1]).Id);

        // Turn was interrupted, so metadata finish reason is NOT normally completed ("stop")
        var metadata = assistantMsg.Get<MessageMetadata>();
        Assert.NotEqual("stop", metadata?.FinishReason);
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
        var executor = new ReActWorkflow(llm, tooling);
        var context = new MockMemoryProvider();

        await foreach (var _ in executor.ExecuteAsync(context, new Text("Run"), null)) { }

        // Tool was only executed once despite duplicate call ID
        Assert.Equal(1, executionCount);

        // Also test recovery scenario: pre-existing ToolResult in context prevents execution
        var recoveryContext = new MockMemoryProvider();
        await recoveryContext.AddAsync([
            new Message(Role.Tool, [new ToolResult("call_dup", [new Text("previously executed")])])
        ]);

        provider.Enqueue(
            new ToolCallStart(0, "call_dup", "my_tool"),
            new ToolCallEnd(0),
            new MessageEnd(FinishReason: "stop")
        );

        await foreach (var _ in executor.ExecuteAsync(recoveryContext, new Text("Recover"), null)) { }

        // Count should still be 1 (did not execute again)
        Assert.Equal(1, executionCount);
    }
}
