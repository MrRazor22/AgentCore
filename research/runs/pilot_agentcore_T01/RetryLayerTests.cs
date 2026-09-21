using System.Net;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using Xunit;

namespace PilotAgentCoreT01;

public class RetryLayerTests
{
    private sealed class MockLLM(Func<int, IAsyncEnumerable<IMessageEvent>> streamFactory) : ILLM
    {
        public int CallCount { get; private set; }

        public IAsyncEnumerable<IMessageEvent> GenerateAsync(
            IReadOnlyList<Message> messages,
            IReadOnlyList<ToolDefinition>? tools = null,
            JsonSchema? responseSchema = null,
            CancellationToken ct = default)
        {
            CallCount++;
            return streamFactory(CallCount);
        }
    }

    [Fact]
    public async Task Test1_Transient429_RetriesTwiceAndSucceedsOnAttempt3()
    {
        var mock = new MockLLM(attempt =>
        {
            if (attempt < 3)
            {
                return ThrowBeforeYield(new HttpRequestException("Too Many Requests", null, HttpStatusCode.TooManyRequests));
            }
            return CreateEvents(new TextStart(0), new TextDelta(0, "Success"), new TextEnd(0));
        });

        var layer = new RetryLayer(mock, maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(1), useJitter: false);
        var events = new List<IMessageEvent>();

        await foreach (var evt in layer.GenerateAsync([]))
        {
            events.Add(evt);
        }

        Assert.Equal(3, events.Count);
        Assert.Equal(3, mock.CallCount);
    }

    [Fact]
    public async Task Test2_Fatal400_PropagatesImmediatelyWithoutRetry()
    {
        var mock = new MockLLM(_ => ThrowBeforeYield(new HttpRequestException("Bad Request", null, HttpStatusCode.BadRequest)));
        var layer = new RetryLayer(mock, maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(1));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in layer.GenerateAsync([])) { }
        });

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal(1, mock.CallCount);
    }

    [Fact]
    public async Task Test3_ExceedsMaxRetries_ThrowsException()
    {
        var mock = new MockLLM(_ => ThrowBeforeYield(new HttpRequestException("Service Unavailable", null, HttpStatusCode.ServiceUnavailable)));
        var layer = new RetryLayer(mock, maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(1), useJitter: false);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in layer.GenerateAsync([])) { }
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Equal(4, mock.CallCount);
    }

    [Fact]
    public void Test4_VerifyBackoffDelays()
    {
        var initial = TimeSpan.FromMilliseconds(100);
        var layer = new RetryLayer(maxRetries: 3, initialDelay: initial, backoffMultiplier: 2.0, useJitter: false);

        var delay1 = layer.CalculateDelay(1);
        var delay2 = layer.CalculateDelay(2);
        var delay3 = layer.CalculateDelay(3);

        Assert.Equal(TimeSpan.FromMilliseconds(100), delay1);
        Assert.Equal(TimeSpan.FromMilliseconds(200), delay2);
        Assert.Equal(TimeSpan.FromMilliseconds(400), delay3);

        var jitterLayer = new RetryLayer(maxRetries: 3, initialDelay: initial, backoffMultiplier: 2.0, useJitter: true);
        for (var i = 0; i < 20; i++)
        {
            var jitterDelay2 = jitterLayer.CalculateDelay(2);
            Assert.InRange(jitterDelay2.TotalMilliseconds, 100.0, 200.0);
        }
    }

    private static async IAsyncEnumerable<IMessageEvent> CreateEvents(params object[] events)
    {
        foreach (var evt in events)
        {
            if (evt is IContentEvent ce) yield return new MessageDelta(Content: ce);
            else if (evt is IMessageEvent me) yield return me;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<IMessageEvent> ThrowBeforeYield(Exception ex)
    {
        await Task.Yield();
        throw ex;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
