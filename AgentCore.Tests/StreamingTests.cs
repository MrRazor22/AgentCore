using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AgentCore;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tokens;
using AgentCore.Tooling;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore.Tests;

public class StreamingTests
{
    [Fact]
    public async Task StreamAsync_PartialTokenStreaming_AggregatesCorrectly()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueAction(() => new IContentDelta[]
        {
            new TextDelta("Hello "),
            new TextDelta("world!"),
            new MetaDelta(null, new TokenUsage(10, 5, 0))
        });

        var executor = new LLMExecutor(
            mockProvider,
            new MockToolRegistry(),
            new ApproximateTokenCounter(),
            new MockTokenManager(),
            NullLogger<LLMExecutor>.Instance
        );

        // Act
        var events = new List<LLMEvent>();
        await foreach (var evt in executor.StreamAsync(new List<Message>(), new LLMOptions()))
        {
            events.Add(evt);
        }

        // Assert
        var textDeltas = events.OfType<TextEvent>().Select(e => e.Delta).ToList();
        Assert.Equal(2, textDeltas.Count);
        Assert.Equal("Hello ", textDeltas[0]);
        Assert.Equal("world!", textDeltas[1]);

        var meta = events.OfType<LLMMetaEvent>().Single();
        Assert.Equal(10, meta.Usage.InputTokens);
        Assert.Equal(5, meta.Usage.OutputTokens);
    }

    [Fact]
    public async Task InvokeStreamingAsync_ToolCallStreaming_YieldsCorrectEvents()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueAction(() => new IContentDelta[]
        {
            new ToolCallDelta(0, "call_1", "test_tool", "{\"arg"),
            new ToolCallDelta(0, "call_1", "test_tool", "\": 123}")
        });

        var mockRegistry = new MockToolRegistry();
        mockRegistry.Register(new DelegateTool(
            () => "ToolResponse",
            "test_tool",
            "A mock tool"));

        var executor = new LLMExecutor(
            mockProvider,
            mockRegistry,
            new ApproximateTokenCounter(),
            new MockTokenManager(),
            NullLogger<LLMExecutor>.Instance
        );

        var toolExecutor = new MockToolExecutor();
        var memory = new MockMemory();
        var agent = new LLMAgent(
            executor,
            toolExecutor,
            memory,
            new ApproximateTokenCounter(),
            new LLMOptions(),
            new AgentConfig { Name = "testAgent" },
            NullLogger<LLMAgent>.Instance
        );

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.InvokeStreamingAsync(new Text("Go")))
        {
            events.Add(evt);
        }

        // Assert
        var toolCallEvents = events.OfType<ToolCallEvent>().ToList();
        Assert.Single(toolCallEvents);
        Assert.Equal("test_tool", toolCallEvents[0].Call.Name);
        Assert.Equal("call_1", toolCallEvents[0].Call.Id);

        var toolResultEvents = events.OfType<AgentToolResultEvent>().ToList();
        Assert.Single(toolResultEvents);
        Assert.Equal("call_1", toolResultEvents[0].Result.CallId);
    }

    [Fact]
    public async Task InvokeStreamingAsync_Cancellation_AbortsImmediately()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueAction(() => new IContentDelta[]
        {
            new TextDelta("Part 1"),
            new TextDelta("Part 2")
        });

        var executor = new LLMExecutor(
            mockProvider,
            new MockToolRegistry(),
            new ApproximateTokenCounter(),
            new MockTokenManager(),
            NullLogger<LLMExecutor>.Instance
        );

        var agent = new LLMAgent(
            executor,
            new MockToolExecutor(),
            new MockMemory(),
            new ApproximateTokenCounter(),
            new LLMOptions(),
            new AgentConfig(),
            NullLogger<LLMAgent>.Instance
        );

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var evt in agent.InvokeStreamingAsync(new Text("Hello"), ct: cts.Token))
            {
                // Should abort
            }
        });
    }

    [Fact]
    public async Task InvokeStreamingAsync_AgentErrorEventEmission_OnContextOverflow()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueAction(() => throw new ContextLengthExceededException());

        var executor = new LLMExecutor(
            mockProvider,
            new MockToolRegistry(),
            new ApproximateTokenCounter(),
            new MockTokenManager(),
            NullLogger<LLMExecutor>.Instance
        );

        var agent = new LLMAgent(
            executor,
            new MockToolExecutor(),
            new MockMemory(),
            new ApproximateTokenCounter(),
            new LLMOptions(),
            new AgentConfig(),
            NullLogger<LLMAgent>.Instance
        );

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.InvokeStreamingAsync(new Text("test")))
        {
            events.Add(evt);
        }

        // Assert
        var errorEvents = events.OfType<AgentErrorEvent>().ToList();
        Assert.Single(errorEvents);
        Assert.IsType<ContextLengthExceededException>(errorEvents[0].Error);
    }

    [Fact]
    public async Task InvokeAsync_vs_StreamAsync_Consistency()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueAction(() => new IContentDelta[]
        {
            new TextDelta("Hello world"),
            new MetaDelta(null, new TokenUsage(10, 5, 0))
        });
        mockProvider.EnqueueAction(() => new IContentDelta[]
        {
            new TextDelta("Hello world"),
            new MetaDelta(null, new TokenUsage(10, 5, 0))
        });

        var executor = new LLMExecutor(
            mockProvider,
            new MockToolRegistry(),
            new ApproximateTokenCounter(),
            new MockTokenManager(),
            NullLogger<LLMExecutor>.Instance
        );

        var agent = new LLMAgent(
            executor,
            new MockToolExecutor(),
            new MockMemory(),
            new ApproximateTokenCounter(),
            new LLMOptions(),
            new AgentConfig(),
            NullLogger<LLMAgent>.Instance
        );

        // Act - InvokeAsync
        var invokeResponse = await agent.InvokeAsync(new Text("test"));

        // Act - StreamAsync aggregation
        var streamEvents = new List<AgentEvent>();
        await foreach (var evt in agent.InvokeStreamingAsync(new Text("test")))
        {
            streamEvents.Add(evt);
        }

        // Assert text consistency
        Assert.Equal("Hello world", invokeResponse.ForLlm());
        
        var streamTextEvents = streamEvents.OfType<TextEvent>().Select(e => e.Delta);
        var aggregatedStreamText = string.Join("", streamTextEvents);
        Assert.Equal("Hello world", aggregatedStreamText);

        var meta = streamEvents.OfType<LLMMetaEvent>().Single();
        Assert.Equal(10, meta.Usage.InputTokens);
        Assert.Equal(5, meta.Usage.OutputTokens);
    }

    [Fact]
    public async Task StreamAsync_ProviderThrowsAfterPartialStreaming_NoRetryAndPropagates()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueAction(() => 
        {
            // Stream partial token and then throw retryable exception
            return YieldPartialThenThrow();
        });

        var executor = new LLMExecutor(
            mockProvider,
            new MockToolRegistry(),
            new ApproximateTokenCounter(),
            new MockTokenManager(),
            NullLogger<LLMExecutor>.Instance
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<RetryableException>(async () =>
        {
            await foreach (var evt in executor.StreamAsync(new List<Message>(), new LLMOptions { MaxRetries = 3 }))
            {
                // consume
            }
        });

        Assert.Equal("Failure mid-stream", ex.Message);
        Assert.Equal(1, mockProvider.CallCount); // No retry because content was already yielded
    }

    private static IEnumerable<IContentDelta> YieldPartialThenThrow()
    {
        yield return new TextDelta("Partial Content");
        throw new RetryableException("Failure mid-stream");
    }
}
