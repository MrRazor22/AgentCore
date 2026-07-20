using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AgentCore;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using AgentCore.LLM.Chat;

namespace AgentCore.Tests;

public class WorkflowTests
{
    private (ILLM Llm, IToolService Tooling) CreateServices(MockLLMProvider provider, IToolService tooling)
    {
        return (provider, tooling);
    }

    [Fact]
    public async Task ExecuteAsync_NormalAssistantResponse_StreamsAndYieldsResponse()
    {
        // Arrange
        var provider = new MockLLMProvider();
        provider.Enqueue(
            new Text("Hello "),
            new Text("world!"),
            new MetaDataEvent(FinishReason.Stop, TimeSpan.Zero)
        );

        var (llm, tooling) = CreateServices(provider, new MockTooling());
        var executor = new ReActWorkflow(llm, tooling);
        var conversation = new List<Message> { new Message(Role.User, new Text("Hi")) };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in executor.ExecuteAsync(conversation, responseSchema: null))
        {
            events.Add(evt);
        }

        // Assert
        // Yields only AgentResponseEvent<string>
        var finalResponse = events.OfType<AgentResponseEvent<string>>().Single();
        Assert.Equal("Hello world!", finalResponse.Response);

        // Conversation history updated with Assistant response
        Assert.Equal(2, conversation.Count);
        Assert.Equal(Role.Assistant, conversation[1].Role);
        Assert.Equal("Hello world!", conversation[1].Contents[0].ForLlm());
    }

    [Fact]
    public async Task ExecuteAsync_SingleToolCall_RunsToolAndInvokesLLMAgain()
    {
        // Arrange
        var provider = new MockLLMProvider();
        // First LLM call: return tool call
        provider.Enqueue(
            new ToolCall("call_1", "get_weather", new JsonObject { ["city"] = "London" }),
            new MetaDataEvent(FinishReason.ToolCall, TimeSpan.Zero)
        );
        // Second LLM call: return final text response based on tool result
        provider.Enqueue(
            new Text("It is sunny in London."),
            new MetaDataEvent(FinishReason.Stop, TimeSpan.Zero)
        );

        var tooling = new MockTooling();
        tooling.Handler = (calls, ct) =>
        {
            var results = calls.Select(c => new Message(Role.Tool, new ToolResult(c.Id, new Text("Rainy")))).ToList();
            return Task.FromResult<IReadOnlyList<Message>>(results);
        };

        var (llm, _) = CreateServices(provider, tooling);
        var executor = new ReActWorkflow(llm, tooling);
        var conversation = new List<Message> { new Message(Role.User, new Text("Weather in London?")) };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in executor.ExecuteAsync(conversation, responseSchema: null))
        {
            events.Add(evt);
        }

        // Assert
        // Events check
        Assert.Contains(events, e => e is ToolCallEvent tc && tc.ToolCall.Name == "get_weather");
        Assert.Contains(events, e => e is ToolResultEvent tr && tr.Result.ForLlm() == "Rainy");
        var finalResponse = events.OfType<AgentResponseEvent<string>>().Single();
        Assert.Equal("It is sunny in London.", finalResponse.Response);

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
    public async Task ExecuteAsync_MaxIterationsReached_YieldsWarningAndStops()
    {
        // Arrange
        var provider = new MockLLMProvider();
        // Return tool call indefinitely
        provider.Enqueue(
            new ToolCall("call_1", "looping_tool", new JsonObject()),
            new MetaDataEvent(FinishReason.ToolCall, TimeSpan.Zero)
        );
        provider.Enqueue(
            new ToolCall("call_2", "looping_tool", new JsonObject()),
            new MetaDataEvent(FinishReason.ToolCall, TimeSpan.Zero)
        );

        var (llm, tooling) = CreateServices(provider, new MockTooling());
        // Configure maxIterations = 1
        var executor = new ReActWorkflow(llm, tooling, maxIterations: 1);
        var conversation = new List<Message> { new Message(Role.User, new Text("Loop")) };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in executor.ExecuteAsync(conversation, responseSchema: null))
        {
            events.Add(evt);
        }

        // Assert
        var errorEvent = Assert.Single(events.OfType<ErrorEvent>());
        Assert.IsType<InvalidOperationException>(errorEvent.Error);
        Assert.Contains("exceeded the maximum limit", errorEvent.Error.Message);
        
        Assert.Empty(events.OfType<AgentResponseEvent<string>>());
    }

    [Fact]
    public async Task ExecuteAsync_ToolThrows_PropagatesException()
    {
        // Arrange
        var provider = new MockLLMProvider();
        provider.Enqueue(
            new ToolCall("call_1", "broken_tool", new JsonObject()),
            new MetaDataEvent(FinishReason.ToolCall, TimeSpan.Zero)
        );

        var tooling = new MockTooling();
        tooling.Handler = (calls, ct) => throw new InvalidOperationException("Tool crash");

        var (llm, _) = CreateServices(provider, tooling);
        var executor = new ReActWorkflow(llm, tooling);
        var conversation = new List<Message> { new Message(Role.User, new Text("Run tool")) };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var evt in executor.ExecuteAsync(conversation, responseSchema: null))
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
        var conversation = new List<Message> { new Message(Role.User, new Text("Run")) };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var evt in executor.ExecuteAsync(conversation, responseSchema: null))
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
        provider.Enqueue(new Text("Never streamed"));

        var (llm, tooling) = CreateServices(provider, new MockTooling());
        var executor = new ReActWorkflow(llm, tooling);
        var conversation = new List<Message> { new Message(Role.User, new Text("Cancel me")) };

        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var evt in executor.ExecuteAsync(conversation, responseSchema: null, ct: cts.Token))
            {
                // Consume
            }
        });
    }
}
