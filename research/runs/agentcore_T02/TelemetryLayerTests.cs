using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using Xunit;

namespace AgentCoreT02;

public class TelemetryLayerTests
{
    private sealed class MockLLM(Func<IAsyncEnumerable<IMessageEvent>> streamFactory) : ILLM
    {
        public IAsyncEnumerable<IMessageEvent> GenerateAsync(
            IReadOnlyList<Message> messages,
            IReadOnlyList<ToolDefinition>? tools = null,
            JsonSchema? responseSchema = null,
            CancellationToken ct = default) => streamFactory();
    }

    private sealed class MockToolbox(Func<IReadOnlyList<ToolCall>, IAsyncEnumerable<IMessageEvent>> execFactory) : IToolbox
    {
        public ValueTask<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<ITool>>([]);

        public IAsyncEnumerable<IMessageEvent> ExecuteAsync(
            IReadOnlyList<ToolCall> calls,
            CancellationToken ct = default) => execFactory(calls);
    }

    [Fact]
    public async Task Test1_EmitsEventBeforeAndAfterLLMGenerationWithDurationAndTokens()
    {
        var eventsList = new List<TelemetryRecord>();
        var mock = new MockLLM(() => CreateEvents(
            new MessageStart(Role.Assistant, Id: "m1"),
            new MessageDelta("m1", Content: new TextDelta(0, "Hello world response")),
            new MessageEnd(Id: "m1")
        ));

        var layer = new TelemetryLLMLayer(mock, e => eventsList.Add(e), callerId: "agent-1");
        var messages = new List<Message> { new(Role.User, [new Text("Hi")]) };

        await foreach (var _ in layer.GenerateAsync(messages)) { }

        Assert.Equal(2, eventsList.Count);
        Assert.Equal("LLM", eventsList[0].Category);
        Assert.Equal("Start", eventsList[0].Phase);
        Assert.Equal("agent-1", eventsList[0].CallerId);
        Assert.Null(eventsList[0].Duration);

        Assert.Equal("LLM", eventsList[1].Category);
        Assert.Equal("End", eventsList[1].Phase);
        Assert.NotNull(eventsList[1].Duration);
        Assert.True(eventsList[1].Duration!.Value >= TimeSpan.Zero);
        Assert.True(eventsList[1].Tokens > 0);
        Assert.Equal("Success", eventsList[1].Status);
    }

    [Fact]
    public async Task Test2_EmitsEventBeforeAndAfterToolExecutionWithStatusAndDuration()
    {
        var eventsList = new List<TelemetryRecord>();
        var mock = new MockToolbox(calls => CreateEvents(
            new MessageStart(Role.Tool, Id: calls[0].Id),
            new MessageDelta(calls[0].Id, Content: new ToolResult(calls[0].Id, [new Text("Calculated: 42")])),
            new MessageEnd(Id: calls[0].Id)
        ));

        var layer = new TelemetryToolboxLayer(mock, e => eventsList.Add(e), callerId: "tool-agent");
        var calls = new List<ToolCall> { new("c1", "calculator", "{\"expr\": \"21*2\"}") };

        await foreach (var _ in layer.ExecuteAsync(calls)) { }

        Assert.Equal(2, eventsList.Count);
        Assert.Equal("Tool", eventsList[0].Category);
        Assert.Equal("Start", eventsList[0].Phase);
        Assert.Equal("tool-agent", eventsList[0].CallerId);

        Assert.Equal("Tool", eventsList[1].Category);
        Assert.Equal("End", eventsList[1].Phase);
        Assert.NotNull(eventsList[1].Duration);
        Assert.Equal("Success", eventsList[1].Status);
    }

    [Fact]
    public async Task Test3_PassesThroughExecutionPayloadsWithoutMutatingEvents()
    {
        var originalDelta = new MessageDelta("m1", Content: new TextDelta(0, "Unmutated delta"));
        var mock = new MockLLM(() => CreateEvents(originalDelta));
        var layer = new TelemetryLLMLayer(mock, _ => { });

        var received = new List<IMessageEvent>();
        await foreach (var evt in layer.GenerateAsync([]))
        {
            received.Add(evt);
        }

        Assert.Single(received);
        Assert.Same(originalDelta, received[0]);
    }

    [Fact]
    public async Task Test4_DoesNotLeakCredentialsOrSecretsInStructuredLogs()
    {
        var eventsList = new List<TelemetryRecord>();
        var mock = new MockLLM(() => CreateEvents(new MessageDelta("m1", Content: new TextDelta(0, "ok"))));
        var layer = new TelemetryLLMLayer(mock, e => eventsList.Add(e));

        var secretPrompt = "Please connect with apiKey: sk-secret-1234567890abcdef and password: supersecretpass";
        var messages = new List<Message> { new(Role.User, [new Text(secretPrompt)]) };

        await foreach (var _ in layer.GenerateAsync(messages)) { }

        var startRecord = eventsList.First(e => e.Phase == "Start");
        Assert.NotNull(startRecord.SanitizedPayload);
        Assert.DoesNotContain("sk-secret-1234567890abcdef", startRecord.SanitizedPayload);
        Assert.DoesNotContain("supersecretpass", startRecord.SanitizedPayload);
        Assert.Contains("[REDACTED]", startRecord.SanitizedPayload);
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
