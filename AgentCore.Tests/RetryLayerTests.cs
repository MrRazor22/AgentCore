using System.Net;
using AgentCore.Layers.LLM;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using Xunit;

namespace AgentCore.Tests;

public class RetryLayerTests
{
    private class MockLLM : ILLM
    {
        private readonly Func<int, IAsyncEnumerable<IMessageEvent>> _streamFactory;
        public int CallCount { get; private set; }

        public MockLLM(Func<int, IAsyncEnumerable<IMessageEvent>> streamFactory)
        {
            _streamFactory = streamFactory;
        }

        public IAsyncEnumerable<IMessageEvent> StreamAsync(
            IReadOnlyList<Message> messages,
            JsonSchema? responseSchema = null,
            IReadOnlyList<ToolDefinition>? tools = null,
            CancellationToken ct = default)
        {
            CallCount++;
            return _streamFactory(CallCount);
        }
    }

    [Fact]
    public async Task StreamAsync_SuccessfulFirstAttempt_YieldsEventsWithoutRetry()
    {
        var mockLLM = new MockLLM(_ => CreateAsyncEnumerable(new TextStart(0), new TextDelta(0, "Hello"), new TextEnd(0)));
        var layer = new RetryLayer(maxRetries: 3);
        layer.Attach(mockLLM);

        var events = new List<IMessageEvent>();
        await foreach (var evt in layer.StreamAsync([]))
        {
            events.Add(evt);
        }

        Assert.Equal(3, events.Count);
        Assert.Equal(1, mockLLM.CallCount);
    }

    [Fact]
    public async Task StreamAsync_TransientErrorBeforeEmission_RetriesAndSucceeds()
    {
        var mockLLM = new MockLLM(attempt =>
        {
            if (attempt < 3)
            {
                return ThrowBeforeYield(new HttpRequestException("Server error", null, HttpStatusCode.ServiceUnavailable));
            }
            return CreateAsyncEnumerable(new TextStart(0), new TextDelta(0, "Recovered"), new TextEnd(0));
        });

        var attemptsRetried = new List<int>();
        var layer = new RetryLayer(
            maxRetries: 3,
            initialDelay: TimeSpan.FromMilliseconds(5),
            useJitter: false,
            onRetry: (ex, attempt, delay) => attemptsRetried.Add(attempt));
        layer.Attach(mockLLM);

        var events = new List<IMessageEvent>();
        await foreach (var evt in layer.StreamAsync([]))
        {
            events.Add(evt);
        }

        Assert.Equal(3, events.Count);
        Assert.Equal(3, mockLLM.CallCount);
        Assert.Equal(2, attemptsRetried.Count);
        Assert.Equal(1, attemptsRetried[0]);
        Assert.Equal(2, attemptsRetried[1]);
    }

    [Fact]
    public async Task StreamAsync_TransientErrorExceedsMaxRetries_ThrowsLastException()
    {
        var mockLLM = new MockLLM(_ => ThrowBeforeYield(new HttpRequestException("Rate limit", null, HttpStatusCode.TooManyRequests)));
        var layer = new RetryLayer(
            maxRetries: 2,
            initialDelay: TimeSpan.FromMilliseconds(1),
            useJitter: false);
        layer.Attach(mockLLM);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in layer.StreamAsync([])) { }
        });

        Assert.Equal("Rate limit", ex.Message);
        Assert.Equal(3, mockLLM.CallCount); // Attempt 1 + 2 retries = 3 attempts total
    }

    [Fact]
    public async Task StreamAsync_NonTransientError_ThrowsImmediatelyWithoutRetrying()
    {
        var mockLLM = new MockLLM(_ => ThrowBeforeYield(new InvalidOperationException("Fatal configuration error")));
        var layer = new RetryLayer(
            maxRetries: 3,
            initialDelay: TimeSpan.FromMilliseconds(1));
        layer.Attach(mockLLM);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in layer.StreamAsync([])) { }
        });

        Assert.Equal(1, mockLLM.CallCount);
    }

    [Fact]
    public async Task StreamAsync_FailureAfterEmittingEvent_DoesNotRetryAndThrowsImmediately()
    {
        var mockLLM = new MockLLM(_ => ThrowAfterYield(new TextStart(0), new HttpRequestException("Disconnect mid-stream", null, HttpStatusCode.BadGateway)));
        var layer = new RetryLayer(
            maxRetries: 3,
            initialDelay: TimeSpan.FromMilliseconds(1));
        layer.Attach(mockLLM);

        var eventsReceived = new List<IMessageEvent>();

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var evt in layer.StreamAsync([]))
            {
                eventsReceived.Add(evt);
            }
        });

        // Verifies the first event was yielded and no retry was attempted
        Assert.Single(eventsReceived);
        Assert.Equal(1, mockLLM.CallCount);
    }

    [Fact]
    public async Task StreamAsync_CustomPredicate_ControlsRetryDecision()
    {
        var mockLLM = new MockLLM(attempt =>
        {
            if (attempt == 1)
            {
                return ThrowBeforeYield(new CustomBusinessException("Recoverable custom error"));
            }
            return CreateAsyncEnumerable(new TextStart(0), new TextEnd(0));
        });

        var layer = new RetryLayer(
            maxRetries: 2,
            initialDelay: TimeSpan.FromMilliseconds(1),
            shouldRetry: (ex, attempt) => ex is CustomBusinessException);
        layer.Attach(mockLLM);

        var events = new List<IMessageEvent>();
        await foreach (var evt in layer.StreamAsync([]))
        {
            events.Add(evt);
        }

        Assert.Equal(2, events.Count);
        Assert.Equal(2, mockLLM.CallCount);
    }

    [Theory]
    [InlineData(-1, 1000, 30000, 2.0)]
    [InlineData(3, -1, 30000, 2.0)]
    [InlineData(3, 1000, -1, 2.0)]
    [InlineData(3, 1000, 30000, 0.5)]
    [InlineData(3, 1000, 30000, double.NaN)]
    [InlineData(3, 1000, 30000, double.PositiveInfinity)]
    public void Constructor_InvalidOptions_ThrowsArgumentOutOfRangeException(int maxRetries, int initialMs, int maxMs, double multiplier)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryLayer(
            maxRetries: maxRetries,
            initialDelay: TimeSpan.FromMilliseconds(initialMs),
            maxDelay: TimeSpan.FromMilliseconds(maxMs),
            backoffMultiplier: multiplier));
    }

    [Fact]
    public void IsTransient_ClassifiesStandardExceptionsCorrectly()
    {
        Assert.True(RetryLayer.IsTransient(new TimeoutException("test")));
        Assert.True(RetryLayer.IsTransient(new IOException("test")));
        Assert.True(RetryLayer.IsTransient(new HttpRequestException("network down")));
        Assert.True(RetryLayer.IsTransient(new HttpRequestException("429", null, HttpStatusCode.TooManyRequests)));
        Assert.True(RetryLayer.IsTransient(new HttpRequestException("503", null, HttpStatusCode.ServiceUnavailable)));
        Assert.True(RetryLayer.IsTransient(new HttpRequestException("500", null, HttpStatusCode.InternalServerError)));

        Assert.False(RetryLayer.IsTransient(new HttpRequestException("400", null, HttpStatusCode.BadRequest)));
        Assert.False(RetryLayer.IsTransient(new HttpRequestException("401", null, HttpStatusCode.Unauthorized)));
        Assert.False(RetryLayer.IsTransient(new HttpRequestException("403", null, HttpStatusCode.Forbidden)));
        Assert.False(RetryLayer.IsTransient(new HttpRequestException("404", null, HttpStatusCode.NotFound)));
        Assert.False(RetryLayer.IsTransient(new ArgumentNullException()));
    }

    private class CustomBusinessException : Exception
    {
        public CustomBusinessException(string message) : base(message) { }
    }

    private static async IAsyncEnumerable<IMessageEvent> CreateAsyncEnumerable(params IMessageEvent[] events)
    {
        foreach (var evt in events)
        {
            yield return evt;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<IMessageEvent> ThrowBeforeYield(Exception ex)
    {
        await Task.Yield();
        throw ex;
#pragma warning disable CS0162 // Unreachable code detected
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<IMessageEvent> ThrowAfterYield(IMessageEvent firstEvent, Exception ex)
    {
        yield return firstEvent;
        await Task.Yield();
        throw ex;
    }
}
