using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Exceptions;
using AgentCore.LLM;
using AgentCore.Tools;
using AgentCore.LLM.Conversation;

namespace AgentCore.Tests;

public class RetryTests
{
    [Fact]
    public async Task StreamAsync_RetriesOnTransientErrorAndSucceeds()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        // 1st attempt: transient failure
        mockProvider.EnqueueException(new RetryableException("Transient error"));
        // 2nd attempt: success
        mockProvider.Enqueue(new TextDelta("Success text"));

        var service = new LLMService(
            mockProvider,
            new ToolRegistry(),
            new ApproximateTokenCounter(),
            maxRetries: 1 // Allow 1 retry (2 attempts total)
        );

        // Act
        var events = new List<LLMEvent>();
        await foreach (var evt in service.StreamAsync(new List<Message>()))
        {
            events.Add(evt);
        }

        // Assert
        Assert.Equal(2, mockProvider.CallCount);
        var textEvent = events.OfType<TextEvent>().Single();
        Assert.Equal("Success text", textEvent.Delta);
    }

    [Fact]
    public async Task StreamAsync_ThrowsWhenRetriesExhausted()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueException(new RetryableException("Transient error 1"));
        mockProvider.EnqueueException(new RetryableException("Transient error 2"));

        var service = new LLMService(
            mockProvider,
            new ToolRegistry(),
            new ApproximateTokenCounter(),
            maxRetries: 1 // Allow 1 retry (2 attempts total)
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<RetryableException>(async () =>
        {
            await foreach (var evt in service.StreamAsync(new List<Message>()))
            {
                // Consume
            }
        });

        Assert.Equal(2, mockProvider.CallCount);
        Assert.Equal("Transient error 2", ex.Message);
    }

    [Fact]
    public async Task StreamAsync_DoesNotRetryFatalExceptions()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueException(new InvalidOperationException("Fatal error"));

        var service = new LLMService(
            mockProvider,
            new ToolRegistry(),
            new ApproximateTokenCounter(),
            maxRetries: 2
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var evt in service.StreamAsync(new List<Message>()))
            {
                // Consume
            }
        });

        Assert.Equal(1, mockProvider.CallCount);
        Assert.Equal("Fatal error", ex.Message);
    }

    [Fact]
    public async Task StreamAsync_DoesNotRetryAfterFirstDeltaYielded()
    {
        var mockProvider = new MockLLMProvider();
        
        // Enqueue deltas directly rather than a complex delegate to avoid CS1622/return errors
        // By throwing after the first text delta is streamed, we verify that StreamAsync handles mid-stream failure.
        // Let's implement this by using the Func enqueue overload that is safe.
        mockProvider.Enqueue(ct => 
        {
            async IAsyncEnumerable<IContentDelta> YieldAndThrow()
            {
                yield return new TextDelta("First delta");
                await Task.Yield();
                throw new RetryableException("Transient error after yield");
            }
            return YieldAndThrow();
        });

        var service = new LLMService(
            mockProvider,
            new ToolRegistry(),
            new ApproximateTokenCounter(),
            maxRetries: 3
        );

        var events = new List<LLMEvent>();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<RetryableException>(async () =>
        {
            await foreach (var evt in service.StreamAsync(new List<Message>()))
            {
                events.Add(evt);
            }
        });

        // The first text delta should have been processed
        Assert.Contains(events, e => e is TextEvent te && te.Delta == "First delta");
        
        // Assert that call count is exactly 1 (no retries were attempted since hasYielded was set)
        Assert.Equal(1, mockProvider.CallCount);
        Assert.Equal("Transient error after yield", ex.Message);
    }
}
