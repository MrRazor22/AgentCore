using System.Diagnostics;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using Xunit;

namespace AgentCoreT05;

public class RateLimitLayerTests
{
    private sealed class MockLLM : ILLM
    {
        public int CallCount { get; private set; }

        public async IAsyncEnumerable<IMessageEvent> GenerateAsync(
            IReadOnlyList<Message> messages,
            IReadOnlyList<ToolDefinition>? tools = null,
            JsonSchema? responseSchema = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);
            yield return new MessageDelta("m1", Content: new TextDelta(0, "response"));
            await Task.Yield();
        }

        private int _callCount;
        public int TotalCalls => _callCount;
    }

    [Fact]
    public async Task Test1_EnforcesMaximumCallRateAndBurstCapacity()
    {
        // Burst capacity = 3, refill = 10 per second
        var bucket = new TokenBucket(capacity: 3, refillPerSecond: 10);
        var mock = new MockLLM();
        var layer = new RateLimitLayer(bucket, blocking: false, inner: mock);

        // First 3 should succeed immediately within burst
        for (int i = 0; i < 3; i++)
        {
            await foreach (var _ in layer.GenerateAsync([])) { }
        }

        Assert.Equal(3, mock.TotalCalls);

        // 4th immediate call should exceed burst capacity
        await Assert.ThrowsAsync<RateLimitExceededException>(async () =>
        {
            await foreach (var _ in layer.GenerateAsync([])) { }
        });
    }

    [Fact]
    public async Task Test2_DelaysOrThrottlesRequestUntilCapacityReplenishes()
    {
        // Burst capacity = 1, refill = 5 per sec (takes ~200ms for 1 token)
        var bucket = new TokenBucket(capacity: 1, refillPerSecond: 5);
        var mock = new MockLLM();
        var layer = new RateLimitLayer(bucket, blocking: true, maxWait: TimeSpan.FromSeconds(2), inner: mock);

        var sw = Stopwatch.StartNew();
        // Call 1 consumes the 1 token immediately
        await foreach (var _ in layer.GenerateAsync([])) { }

        // Call 2 must wait for refill
        await foreach (var _ in layer.GenerateAsync([])) { }
        sw.Stop();

        Assert.Equal(2, mock.TotalCalls);
        Assert.True(sw.ElapsedMilliseconds >= 150, $"Expected wait >= 150ms, got {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Test3_ThrowsRateLimitExceededExceptionWhenNonBlockingOrMaxWaitExceeded()
    {
        var bucket = new TokenBucket(capacity: 1, refillPerSecond: 0.1); // refills 1 token every 10 seconds
        var mock = new MockLLM();
        var layer = new RateLimitLayer(bucket, blocking: true, maxWait: TimeSpan.FromMilliseconds(50), inner: mock);

        // Call 1 succeeds
        await foreach (var _ in layer.GenerateAsync([])) { }

        // Call 2 needs 10 seconds, but maxWait is 50ms -> throws
        await Assert.ThrowsAsync<RateLimitExceededException>(async () =>
        {
            await foreach (var _ in layer.GenerateAsync([])) { }
        });
    }

    [Fact]
    public async Task Test4_ThreadSafeTokenConsumptionUnderConcurrentRequests()
    {
        const int concurrentTasks = 20;
        const int capacity = 10;
        var bucket = new TokenBucket(capacity: capacity, refillPerSecond: 0.001); // virtually no refill during test
        var mock = new MockLLM();
        var layer = new RateLimitLayer(bucket, blocking: false, inner: mock);

        var successCount = 0;
        var failureCount = 0;

        await Parallel.ForEachAsync(Enumerable.Range(0, concurrentTasks), async (i, ct) =>
        {
            try
            {
                await foreach (var _ in layer.GenerateAsync([], ct: ct)) { }
                Interlocked.Increment(ref successCount);
            }
            catch (RateLimitExceededException)
            {
                Interlocked.Increment(ref failureCount);
            }
        });

        Assert.Equal(capacity, successCount);
        Assert.Equal(concurrentTasks - capacity, failureCount);
        Assert.Equal(capacity, mock.TotalCalls);
    }
}
