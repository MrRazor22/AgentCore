using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AgentCore;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore.Tests;

public class WorkflowTests
{
    private (ILLMService Llm, IToolService Tooling) CreateServices(MockLLMProvider provider, IToolService tooling)
    {
        var tokenCounter = new ApproximateTokenCounter();
        var registry = new ToolRegistry();
        var llm = new LLMService(provider, registry, tokenCounter, maxRetries: 1);
        return (llm, tooling);
    }

    [Fact]
    public async Task ExecuteAsync_NormalAssistantResponse_StreamsAndYieldsResponse()
    {
        // Arrange
        var provider = new MockLLMProvider();
        provider.Enqueue(
            new TextDelta("Hello "),
            new TextDelta("world!"),
            new MetaDelta(FinishReason.Stop, 10, 10)
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
        // Text events
        var textEvents = events.OfType<TextEvent>().ToList();
        Assert.Equal(2, textEvents.Count);
        Assert.Equal("Hello ", textEvents[0].Delta);
        Assert.Equal("world!", textEvents[1].Delta);

        // Final AgentResponseEvent
        var finalResponse = events.OfType<AgentResponseEvent>().Single();
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
            new ToolCallDelta(0, "call_1", "get_weather", "{\"city\":\"London\"}"),
            new MetaDelta(FinishReason.ToolCall, 10, 5)
        );
        // Second LLM call: return final text response based on tool result
        provider.Enqueue(
            new TextDelta("It is sunny in London."),
            new MetaDelta(FinishReason.Stop, 25, 10)
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
        Assert.Contains(events, e => e is ToolCallEvent tc && tc.Call.Name == "get_weather");
        Assert.Contains(events, e => e is ToolResultEvent tr && tr.Result.ForLlm() == "Rainy");
        var finalResponse = events.OfType<AgentResponseEvent>().Single();
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
            new ToolCallDelta(0, "call_1", "looping_tool", "{}"),
            new MetaDelta(FinishReason.ToolCall, 10, 5)
        );
        provider.Enqueue(
            new ToolCallDelta(0, "call_2", "looping_tool", "{}"),
            new MetaDelta(FinishReason.ToolCall, 15, 5)
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
        
        Assert.Empty(events.OfType<AgentResponseEvent>());
    }

    [Fact]
    public async Task ExecuteAsync_ToolThrows_PropagatesException()
    {
        // Arrange
        var provider = new MockLLMProvider();
        provider.Enqueue(
            new ToolCallDelta(0, "call_1", "broken_tool", "{}"),
            new MetaDelta(FinishReason.ToolCall, 10, 5)
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
        provider.Enqueue(new TextDelta("Never streamed"));

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

    [Fact]
    public async Task ExecuteAsync_IncrementalStreaming_YieldsEventsImmediately()
    {
        // Arrange
        var provider = new MockLLMProvider();
        provider.Enqueue(
            new TextDelta("A"),
            new ReasoningDelta("Thought"),
            new TextDelta("B")
        );

        var (llm, tooling) = CreateServices(provider, new MockTooling());
        var executor = new ReActWorkflow(llm, tooling);
        var conversation = new List<Message> { new Message(Role.User, new Text("Stream check")) };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in executor.ExecuteAsync(conversation, responseSchema: null))
        {
            events.Add(evt);
        }

        // Assert
        Assert.Equal(5, events.Count); // Text("A"), Reasoning("Thought"), Text("B"), MetaDataEvent, AgentResponseEvent("AB")
        Assert.Equal("A", ((TextEvent)events[0]).Delta);
        Assert.Equal("Thought", ((ReasoningEvent)events[1]).Delta);
        Assert.Equal("B", ((TextEvent)events[2]).Delta);
        Assert.IsType<MetaDataEvent>(events[3]);
        Assert.Equal("AB", ((AgentResponseEvent)events[4]).Response);
    }
}
