using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
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
        provider.Enqueue(new TextDelta("Today is sunny."));

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
            new ToolCallDelta("call_1", "get_weather", "{\"location\": \"London\"}"),
            new Metadata(FinishReason: "tool_calls")
        );
        // Second LLM call yields final response
        provider.Enqueue(
            new TextDelta("It is sunny in London."),
            new Metadata(FinishReason: "stop")
        );

        var tooling = new MockTooling();
        tooling.Handler = (calls, ct) =>
        {
            var results = calls.Select(c => new Message(Role.Tool, new ToolResult(c.Id, new Text("Rainy")))).ToList();
            return Task.FromResult<IReadOnlyList<Message>>(results);
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
            new ToolCallDelta("call_1", "looping_tool", "{}"),
            new Metadata(FinishReason: "tool_calls")
        );
        provider.Enqueue(
            new ToolCallDelta("call_2", "looping_tool", "{}"),
            new Metadata(FinishReason: "tool_calls")
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
            new ToolCallDelta("call_1", "broken_tool", "{}"),
            new Metadata(FinishReason: "tool_calls")
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
        provider.Enqueue(new TextDelta("Never streamed"));

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
}
