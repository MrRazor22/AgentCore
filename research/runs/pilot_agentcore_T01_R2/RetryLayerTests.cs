using System.Net;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using Xunit;

namespace PilotAgentCoreT01R2;

public class RetryLayerTests
{
    private sealed class MockLLM(Func<int, IAsyncEnumerable<IMessageEvent>> generator) : ILLM
    {
        public int InvocationCount { get; private set; }

        public IAsyncEnumerable<IMessageEvent> GenerateAsync(
            IReadOnlyList<Message> messages,
            IReadOnlyList<ToolDefinition>? tools = null,
            JsonSchema? responseSchema = null,
            CancellationToken ct = default)
        {
            InvocationCount++;
            return generator(InvocationCount);
        }
    }

    private static async IAsyncEnumerable<IMessageEvent> SuccessfulStream(string content = "OK")
    {
        yield return new MessageStart(Role.Assistant, "test-msg-1");
        yield return new MessageDelta("test-msg-1", Content: new TextDelta(0, content));
        yield return new MessageEnd("test-msg-1");
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<IMessageEvent> ThrowingStream(Exception ex)
    {
        await Task.Yield();
        if (ex != null)
            throw ex;
        yield break;
    }

    [Fact]
    public async Task Test1_CatchesTransient429_AndSucceedsOnRetry()
    {
        var recordedDelays = new List<TimeSpan>();
        var mock = new MockLLM(attempt =>
        {
            if (attempt == 1)
                return ThrowingStream(new HttpRequestException("Too Many Requests", null, HttpStatusCode.TooManyRequests));
            return SuccessfulStream("Succeeded after 429");
        });

        var layer = new RetryLayer(
            inner: mock,
            maxRetries: 3,
            initialDelay: TimeSpan.FromMilliseconds(50),
            jitterRatio: 0.0,
            sleep: (delay, _) =>
            {
                recordedDelays.Add(delay);
                return Task.CompletedTask;
            });

        var results = new List<IMessageEvent>();
        await foreach (var evt in layer.GenerateAsync([]))
        {
            results.Add(evt);
        }

        Assert.Equal(2, mock.InvocationCount);
        Assert.Single(recordedDelays);
        Assert.Equal(TimeSpan.FromMilliseconds(50), recordedDelays[0]);
        Assert.Contains(results, r => r is MessageDelta d && d.Content is TextDelta td && td.Text == "Succeeded after 429");
    }

    [Fact]
    public async Task Test2_FatalError400_PropagatesImmediatelyWithoutRetry()
    {
        var recordedDelays = new List<TimeSpan>();
        var mock = new MockLLM(_ => ThrowingStream(new HttpRequestException("Bad Request", null, HttpStatusCode.BadRequest)));

        var layer = new RetryLayer(
            inner: mock,
            maxRetries: 3,
            initialDelay: TimeSpan.FromMilliseconds(50),
            jitterRatio: 0.0,
            sleep: (delay, _) =>
            {
                recordedDelays.Add(delay);
                return Task.CompletedTask;
            });

        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in layer.GenerateAsync([])) { }
        });

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal(1, mock.InvocationCount);
        Assert.Empty(recordedDelays);
    }

    [Fact]
    public async Task Test3_ExceedingMaxRetries_ThrowsException()
    {
        var recordedDelays = new List<TimeSpan>();
        var mock = new MockLLM(_ => ThrowingStream(new HttpRequestException("Service Unavailable", null, HttpStatusCode.ServiceUnavailable)));

        var layer = new RetryLayer(
            inner: mock,
            maxRetries: 3,
            initialDelay: TimeSpan.FromMilliseconds(100),
            jitterRatio: 0.0,
            sleep: (delay, _) =>
            {
                recordedDelays.Add(delay);
                return Task.CompletedTask;
            });

        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in layer.GenerateAsync([])) { }
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Equal(4, mock.InvocationCount); // Initial call + 3 retries
        Assert.Equal(3, recordedDelays.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(100), recordedDelays[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(200), recordedDelays[1]);
        Assert.Equal(TimeSpan.FromMilliseconds(400), recordedDelays[2]);
    }

    [Fact]
    public void Test4_BackoffDurationCalculation()
    {
        var baseDelay = TimeSpan.FromMilliseconds(100);

        var delay1 = RetryLayer.CalculateDelay(1, baseDelay, jitterRatio: 0.0);
        var delay2 = RetryLayer.CalculateDelay(2, baseDelay, jitterRatio: 0.0);
        var delay3 = RetryLayer.CalculateDelay(3, baseDelay, jitterRatio: 0.0);
        var delay4 = RetryLayer.CalculateDelay(4, baseDelay, jitterRatio: 0.0);

        Assert.Equal(TimeSpan.FromMilliseconds(100), delay1);
        Assert.Equal(TimeSpan.FromMilliseconds(200), delay2);
        Assert.Equal(TimeSpan.FromMilliseconds(400), delay3);
        Assert.Equal(TimeSpan.FromMilliseconds(800), delay4);

        var rng = new Random(12345);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var expectedBaseMs = 100.0 * Math.Pow(2, attempt - 1);
            var jitterRatio = 0.25;
            var jitteredDelay = RetryLayer.CalculateDelay(attempt, baseDelay, jitterRatio, rng);

            Assert.InRange(jitteredDelay.TotalMilliseconds, expectedBaseMs, expectedBaseMs * (1.0 + jitterRatio));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => RetryLayer.CalculateDelay(0, baseDelay));
    }
}
