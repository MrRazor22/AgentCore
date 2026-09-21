using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using Xunit;

namespace AgentCoreT04;

public class CacheLayerTests
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
    public async Task Test1_ReturnsCachedResponseWithoutInvokingModelOnIdenticalKey()
    {
        var mock = new MockLLM(callIndex => CreateEvents(
            new MessageStart(Role.Assistant, Id: $"m{callIndex}"),
            new MessageDelta($"m{callIndex}", Content: new TextDelta(0, $"Response {callIndex}")),
            new MessageEnd(Id: $"m{callIndex}")
        ));

        var layer = new CacheLayer(mock, ttl: TimeSpan.FromMinutes(5));
        var messages = new List<Message> { new(Role.User, [new Text("What is 2+2?")]) };

        // 1st invocation: Miss
        var results1 = new List<IMessageEvent>();
        await foreach (var evt in layer.GenerateAsync(messages)) results1.Add(evt);

        Assert.Equal(1, mock.CallCount);
        Assert.Equal(1, layer.CacheMissCount);
        Assert.Equal(0, layer.CacheHitCount);

        // 2nd invocation: Hit
        var results2 = new List<IMessageEvent>();
        await foreach (var evt in layer.GenerateAsync(messages)) results2.Add(evt);

        Assert.Equal(1, mock.CallCount); // Still 1! Model was NOT invoked
        Assert.Equal(1, layer.CacheHitCount);
        Assert.Equal(results1.Count, results2.Count);
    }

    [Fact]
    public async Task Test2_InvokesModelAndPopulatesCacheOnCacheMiss()
    {
        var mock = new MockLLM(callIndex => CreateEvents(
            new MessageDelta("m1", Content: new TextDelta(0, "Answer"))
        ));

        var layer = new CacheLayer(mock);
        var messages1 = new List<Message> { new(Role.User, [new Text("Question 1")]) };
        var messages2 = new List<Message> { new(Role.User, [new Text("Question 2")]) };

        await foreach (var _ in layer.GenerateAsync(messages1)) { }
        await foreach (var _ in layer.GenerateAsync(messages2)) { }

        Assert.Equal(2, mock.CallCount);
        Assert.Equal(2, layer.CacheMissCount);
    }

    [Fact]
    public void Test3_IncludesMessageHistoryAndSystemPromptInCacheKeyDerivation()
    {
        var baseMessages = new List<Message>
        {
            new(Role.System, [new Text("You are a helpful assistant")]),
            new(Role.User, [new Text("Hello")])
        };

        var modifiedSystemMessages = new List<Message>
        {
            new(Role.System, [new Text("You are a grumpy assistant")]),
            new(Role.User, [new Text("Hello")])
        };

        var extendedHistoryMessages = new List<Message>
        {
            new(Role.System, [new Text("You are a helpful assistant")]),
            new(Role.User, [new Text("Hello")]),
            new(Role.Assistant, [new Text("Hi there!")]),
            new(Role.User, [new Text("Hello again")])
        };

        var key1 = CacheLayer.DeriveCacheKey(baseMessages);
        var key2 = CacheLayer.DeriveCacheKey(modifiedSystemMessages);
        var key3 = CacheLayer.DeriveCacheKey(extendedHistoryMessages);

        Assert.NotEqual(key1, key2);
        Assert.NotEqual(key1, key3);
        Assert.NotEqual(key2, key3);
    }

    [Fact]
    public async Task Test4_InvalidatesOrBypassesCacheWhenConfiguredTTLExpires()
    {
        var mock = new MockLLM(callIndex => CreateEvents(
            new MessageDelta($"m{callIndex}", Content: new TextDelta(0, $"Version {callIndex}"))
        ));

        var layer = new CacheLayer(mock, ttl: TimeSpan.FromMilliseconds(50));
        var messages = new List<Message> { new(Role.User, [new Text("Time-sensitive question")]) };

        // 1st invocation
        await foreach (var _ in layer.GenerateAsync(messages)) { }
        Assert.Equal(1, mock.CallCount);

        // Wait for TTL to expire
        await Task.Delay(100);

        // 2nd invocation after TTL expiry
        await foreach (var _ in layer.GenerateAsync(messages)) { }
        Assert.Equal(2, mock.CallCount); // Expired -> re-invoked model!
        Assert.Equal(2, layer.CacheMissCount);
    }

    private static async IAsyncEnumerable<IMessageEvent> CreateEvents(params IMessageEvent[] events)
    {
        foreach (var evt in events)
        {
            yield return evt;
            await Task.Yield();
        }
    }
}
