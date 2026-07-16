using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Exceptions;
using AgentCore.Tools;
using AgentCore.LLM.Chat;

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
        mockProvider.Enqueue(new Text("Success text"));

        var service = new LLMService(
            mockProvider,
            Array.Empty<Tool>(),
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
        var textEvent = events.OfType<Text>().Single();
        Assert.Equal("Success text", textEvent.Value);
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
            Array.Empty<Tool>(),
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
            Array.Empty<Tool>(),
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
        
        mockProvider.Enqueue(ct => 
        {
            async IAsyncEnumerable<LLMEvent> YieldAndThrow()
            {
                yield return new Text("First delta");
                await Task.Yield();
                throw new RetryableException("Transient error after yield");
            }
            return YieldAndThrow();
        });

        var service = new LLMService(
            mockProvider,
            Array.Empty<Tool>(),
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
        
        // Assert that call count is exactly 1 (no retries were attempted since hasYielded was set)
        Assert.Equal(1, mockProvider.CallCount);
        Assert.Equal("Transient error after yield", ex.Message);
    }
}
