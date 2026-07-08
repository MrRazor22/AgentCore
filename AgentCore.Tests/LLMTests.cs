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
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore.Tests;

public class LLMTests
{
    [Fact]
    public async Task StreamAsync_RetriesOnTransientErrorsAndSucceeds()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueAction(() => throw new RetryableException("Simulated transient timeout"));
        mockProvider.EnqueueAction(() => throw new RetryableException("Simulated transient 429"));
        mockProvider.EnqueueAction(() => new[] { new TextDelta("Success after retries") });

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
        var textEvents = events.OfType<TextEvent>().ToList();
        Assert.Single(textEvents);
        Assert.Equal("Success after retries", textEvents[0].Delta);
        Assert.Equal(3, mockProvider.CallCount);
    }

    [Fact]
    public async Task StreamAsync_ThrowsWhenRetriesExhausted()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        for (int i = 0; i < 5; i++)
        {
            mockProvider.EnqueueAction(() => throw new RetryableException("Simulated continuous rate limiting"));
        }

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
            await foreach (var evt in executor.StreamAsync(new List<Message>(), new LLMOptions()))
            {
                // Consume
            }
        });

        Assert.Contains("Simulated continuous rate limiting", ex.Message);
        Assert.Equal(4, mockProvider.CallCount); // 1 initial + 3 retries = 4 attempts total
    }

    [Fact]
    public async Task InvokeStreamingAsync_YieldsAgentErrorEventOnContextOverflow()
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

        var memory = new ChatMemory(new ApproximateTokenCounter(), mockProvider);
        var agent = new LLMAgent(
            executor,
            new MockToolExecutor(),
            memory,
            new ApproximateTokenCounter(),
            new LLMOptions { ContextWindow = 2000 },
            new AgentConfig { Name = "testAgent" },
            NullLogger<LLMAgent>.Instance
        );

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.InvokeStreamingAsync(new Text("Hello")))
        {
            events.Add(evt);
        }

        // Assert
        var errorEvents = events.OfType<AgentErrorEvent>().ToList();
        Assert.Single(errorEvents);
        Assert.IsType<ContextLengthExceededException>(errorEvents[0].Error);
    }

    [Fact]
    public async Task InvokeAsync_ThrowsContextLengthExceededExceptionOnContextOverflow()
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

        var memory = new ChatMemory(new ApproximateTokenCounter(), mockProvider);
        var agent = new LLMAgent(
            executor,
            new MockToolExecutor(),
            memory,
            new ApproximateTokenCounter(),
            new LLMOptions { ContextWindow = 2000 },
            new AgentConfig { Name = "testAgent" },
            NullLogger<LLMAgent>.Instance
        );

        // Act & Assert
        await Assert.ThrowsAsync<ContextLengthExceededException>(async () =>
        {
            await agent.InvokeAsync(new Text("Hello"));
        });
    }

    private class MockLLMProvider : ILLMProvider
    {
        private readonly Queue<Func<IEnumerable<IContentDelta>>> _actions = new();
        public int CallCount { get; private set; }

        public void EnqueueAction(Func<IEnumerable<IContentDelta>> action) => _actions.Enqueue(action);

        public async IAsyncEnumerable<IContentDelta> StreamAsync(
            IReadOnlyList<Message> messages,
            LLMOptions options,
            IReadOnlyList<Tool>? tools = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            CallCount++;
            var action = _actions.Count > 0 ? _actions.Dequeue() : () => Enumerable.Empty<IContentDelta>();
            
            var result = action();
            foreach (var item in result)
            {
                yield return item;
            }
            await Task.CompletedTask;
        }
    }

    private class MockToolRegistry : IToolRegistry
    {
        public IReadOnlyList<Tool> Tools => new List<Tool>();
        public void Register(Tool tool) { }
        public bool Unregister(string toolName) => false;
        public Tool? TryGet(string name) => null;
    }

    private class MockTokenManager : ITokenManager
    {
        public void Record(TokenUsage usage) { }
        public TokenUsage GetTotals() => TokenUsage.Empty;
    }

    private class MockToolExecutor : IToolExecutor
    {
        public Task<ToolResult> HandleToolCallAsync(ToolCall call, CancellationToken ct = default)
        {
            return Task.FromResult(new ToolResult(call.Id, new Text("")));
        }
    }
}
