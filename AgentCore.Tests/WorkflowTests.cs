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
        provider.Enqueue(new StreamChunk(new TextChunk("Today is sunny.")));

        var (llm, tooling) = CreateServices(provider, new MockTooling());
        var executor = new ReActWorkflow(llm, tooling);
        var context = new MockMemoryProvider();
        var input = new Text("Hello");

        // Act
        var contents = new List<IContent>();
        await foreach (var item in executor.ExecuteAsync(context, input, responseSchema: null))
        {
            contents.Add(item);
        }

        // Assert
        var textContent = Assert.Single(contents.OfType<Text>());
        Assert.Equal("Today is sunny.", textContent.Value);

        // Assert messages were added to context (User and Assistant)
        var messages = context.Messages;
        Assert.Equal(2, messages.Count);
        Assert.Equal(Role.User, messages[0].Role);
        Assert.Equal("Hello", messages[0].Contents[0].ForLlm());
        Assert.Equal(Role.Assistant, messages[1].Role);
        Assert.Equal("Today is sunny.", messages[1].Contents[0].ForLlm());
    }

    [Fact]
    public async Task ExecuteAsync_WithToolCalls_ExecutesAndResumes()
    {
        // Arrange
        var provider = new MockLLMProvider();
        // First LLM call yields tool call
        provider.Enqueue(
            new StreamChunk(new ToolCallChunk("get_weather", "{\"location\": \"London\"}"), Id: "call_1"),
            new FinishReason("tool_calls")
        );
        // Second LLM call yields final response
        provider.Enqueue(
            new StreamChunk(new TextChunk("It is sunny in London.")),
            new FinishReason("stop")
        );

        var tooling = new MockTooling();
        tooling.Handler = (calls, ct) =>
        {
            var results = calls.Select(c => new ToolResult(c.Id, new Text("Rainy"))).ToList();
            return Task.FromResult<IReadOnlyList<ToolResult>>(results);
        };

        var (llm, _) = CreateServices(provider, tooling);
        var executor = new ReActWorkflow(llm, tooling);
        var context = new MockMemoryProvider();
        var input = new Text("Weather in London?");

        // Act
        var contents = new List<IContent>();
        await foreach (var item in executor.ExecuteAsync(context, input, responseSchema: null))
        {
            contents.Add(item);
        }

        // Assert
        Assert.Contains(contents, c => c is ToolCall tc && tc.Name == "get_weather");
        Assert.Contains(contents, c => c is ToolResult tr && tr.ForLlm() == "Rainy");
        var finalResponse = contents.OfType<Text>().Single();
        Assert.Equal("It is sunny in London.", finalResponse.Value);

        // Verify conversation history captured by provider on the second call
        Assert.Equal(2, provider.CapturedMessages.Count);
        var secondCallHistory = provider.CapturedMessages[1];

        // Should contain User message, Assistant message (with tool call), Tool result message
        Assert.Equal(3, secondCallHistory.Count);
        Assert.Equal(Role.User, secondCallHistory[0].Role);
        Assert.Equal(Role.Assistant, secondCallHistory[1].Role);
        Assert.Equal(Role.Tool, secondCallHistory[2].Role);
        Assert.Equal("Rainy", secondCallHistory[2].Contents[0].ForLlm());
    }

    [Fact]
    public async Task ExecuteAsync_MaxIterationsReached_ThrowsException()
    {
        // Arrange
        var provider = new MockLLMProvider();
        // Return tool call indefinitely
        provider.Enqueue(
            new StreamChunk(new ToolCallChunk("looping_tool", "{}"), Id: "call_1"),
            new FinishReason("tool_calls")
        );
        provider.Enqueue(
            new StreamChunk(new ToolCallChunk("looping_tool", "{}"), Id: "call_2"),
            new FinishReason("tool_calls")
        );

        var (llm, tooling) = CreateServices(provider, new MockTooling());
        var executor = new ReActWorkflow(llm, tooling, maxIterations: 1);
        var context = new MockMemoryProvider();
        var input = new Text("Loop");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var item in executor.ExecuteAsync(context, input, responseSchema: null))
            {
                // Consume
            }
        });

        Assert.Contains("exceeded the maximum limit", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ToolThrows_PropagatesException()
    {
        // Arrange
        var provider = new MockLLMProvider();
        provider.Enqueue(
            new StreamChunk(new ToolCallChunk("broken_tool", "{}"), Id: "call_1"),
            new FinishReason("tool_calls")
        );

        var tooling = new MockTooling();
        tooling.Handler = (calls, ct) => throw new InvalidOperationException("Tool crash");

        var (llm, _) = CreateServices(provider, tooling);
        var executor = new ReActWorkflow(llm, tooling);
        var context = new MockMemoryProvider();
        var input = new Text("Run tool");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var item in executor.ExecuteAsync(context, input, responseSchema: null))
            {
                // Consume
            }
        });
    }

    [Fact]
    public async Task ExecuteAsync_ProviderThrows_PropagatesException()
    {
        // Arrange
        var provider = new MockLLMProvider();
        provider.EnqueueException(new InvalidOperationException("Provider crash"));

        var (llm, tooling) = CreateServices(provider, new MockTooling());
        var executor = new ReActWorkflow(llm, tooling);
        var context = new MockMemoryProvider();
        var input = new Text("Run");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var item in executor.ExecuteAsync(context, input, responseSchema: null))
            {
                // Consume
            }
        });
    }

    [Fact]
    public async Task ExecuteAsync_CanceledToken_StopsImmediately()
    {
        // Arrange
        var provider = new MockLLMProvider();
        provider.Enqueue(new StreamChunk(new TextChunk("Never streamed")));

        var (llm, tooling) = CreateServices(provider, new MockTooling());
        var executor = new ReActWorkflow(llm, tooling);
        var context = new MockMemoryProvider();
        var input = new Text("Cancel me");

        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in executor.ExecuteAsync(context, input, responseSchema: null, ct: cts.Token))
            {
                // Consume
            }
        });
    }

    [Fact]
    public async Task ExecuteAsync_OnStreamCancellation_DoesNotPersistUncompletedAssistantMessage()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        async IAsyncEnumerable<ILLMOutput> CancellationStream([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new StreamChunk(new ReasoningChunk("Thinking..."));
            yield return new StreamChunk(new TextChunk("Hello world partial"));
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            yield return new StreamChunk(new TextChunk(" unreached"));
        }

        var provider = new MockLLMProvider();
        provider.Enqueue(ct => CancellationStream(ct));

        var (llm, tooling) = CreateServices(provider, new MockTooling());
        var executor = new ReActWorkflow(llm, tooling);
        var context = new MockMemoryProvider();
        var input = new Text("User input");

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in executor.ExecuteAsync(context, input, responseSchema: null, ct: cts.Token))
            {
                // Consume stream
            }
        });

        // Assert that uncompleted Assistant message was NOT persisted to context upon cancellation
        Assert.Empty(context.Messages);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleToolCalls_ExecutesConcurrentlyAndYieldsAsCompleted()
    {
        // Arrange
        var provider = new MockLLMProvider();
        provider.Enqueue(
            new StreamChunk(new ToolCallChunk("slow_tool", "{}"), Id: "call_slow"),
            new StreamChunk(new ToolCallChunk("fast_tool", "{}"), Id: "call_fast"),
            new FinishReason("tool_calls")
        );
        provider.Enqueue(
            new StreamChunk(new TextChunk("Both done.")),
            new FinishReason("stop")
        );

        var tooling = new MockTooling();
        tooling.Handler = async (calls, ct) =>
        {
            var call = calls.First();
            if (call.Name == "slow_tool")
            {
                await Task.Delay(150, ct);
                return new List<ToolResult> { new(call.Id, new Text("SlowResult")) };
            }
            else
            {
                await Task.Delay(20, ct);
                return new List<ToolResult> { new(call.Id, new Text("FastResult")) };
            }
        };

        var (llm, _) = CreateServices(provider, tooling);
        var executor = new ReActWorkflow(llm, tooling);
        var context = new MockMemoryProvider();
        var input = new Text("Run concurrent tools");

        // Act
        var contents = new List<IContent>();
        await foreach (var item in executor.ExecuteAsync(context, input, responseSchema: null))
        {
            contents.Add(item);
        }

        // Assert
        var results = contents.OfType<ToolResult>().ToList();
        Assert.Equal(2, results.Count);
        // Fast tool should finish first and yield first even though it was emitted second
        Assert.Equal("call_fast", results[0].CallId);
        Assert.Equal("call_slow", results[1].CallId);
    }
}

