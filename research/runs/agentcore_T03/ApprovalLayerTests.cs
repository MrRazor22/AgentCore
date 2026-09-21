using AgentCore;
using AgentCore.LLM.Chat;
using AgentCore.Tool;
using Xunit;

namespace AgentCoreT03;

public class ApprovalLayerTests
{
    private sealed class MockToolbox(Func<IReadOnlyList<ToolCall>, IAsyncEnumerable<IMessageEvent>> execFactory) : IToolbox
    {
        public int CallCount { get; private set; }

        public ValueTask<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<ITool>>([]);

        public IAsyncEnumerable<IMessageEvent> ExecuteAsync(
            IReadOnlyList<ToolCall> calls,
            CancellationToken ct = default)
        {
            CallCount++;
            return execFactory(calls);
        }
    }

    [Fact]
    public async Task Test1_InterceptsToolsTaggedAsSensitivePriorToInvocation()
    {
        var prompted = false;
        var mock = new MockToolbox(calls => CreateEvents(
            new MessageDelta(calls[0].Id, Content: new ToolResult(calls[0].Id, [new Text("Success")]))
        ));

        var layer = new ApprovalLayer(
            approver: (call, ct) =>
            {
                prompted = true;
                return Task.FromResult(true);
            },
            isSensitive: call => call.Name.StartsWith("dangerous_"),
            inner: mock);

        var calls = new List<ToolCall> { new("c1", "dangerous_delete_db", "{}") };
        await foreach (var _ in layer.ExecuteAsync(calls)) { }

        Assert.True(prompted);
        Assert.Equal(1, mock.CallCount);
    }

    [Fact]
    public async Task Test2_ExecutesToolNormallyWhenApprovalDelegateReturnsTrue()
    {
        var mock = new MockToolbox(calls => CreateEvents(
            new MessageStart(Role.Tool, Id: calls[0].Id),
            new MessageDelta(calls[0].Id, Content: new ToolResult(calls[0].Id, [new Text("Refund processed: $50")])),
            new MessageEnd(Id: calls[0].Id)
        ));

        var layer = new ApprovalLayer(
            approver: (_, _) => Task.FromResult(true),
            isSensitive: _ => true,
            inner: mock);

        var calls = new List<ToolCall> { new("c1", "issue_refund", "{\"amount\": 50}") };
        var results = new List<IMessageEvent>();

        await foreach (var evt in layer.ExecuteAsync(calls))
        {
            results.Add(evt);
        }

        Assert.Equal(1, mock.CallCount);
        var delta = results.OfType<MessageDelta>().FirstOrDefault();
        Assert.NotNull(delta);
        var toolResult = Assert.IsType<ToolResult>(delta.Content);
        Assert.False(toolResult.IsError);
        Assert.Contains("Refund processed", toolResult.Contents.OfType<Text>().First().Value);
    }

    [Fact]
    public async Task Test3_AbortsToolExecutionCleanlyAndReturnsRejectionWhenApprovalReturnsFalse()
    {
        var mock = new MockToolbox(calls => CreateEvents(
            new MessageDelta(calls[0].Id, Content: new ToolResult(calls[0].Id, [new Text("Should not execute")]))
        ));

        var layer = new ApprovalLayer(
            approver: (_, _) => Task.FromResult(false),
            isSensitive: _ => true,
            inner: mock);

        var calls = new List<ToolCall> { new("c1", "execute_sql", "{\"query\": \"DROP TABLE users;\"}") };
        var results = new List<IMessageEvent>();

        await foreach (var evt in layer.ExecuteAsync(calls))
        {
            results.Add(evt);
        }

        Assert.Equal(0, mock.CallCount); // Underlying tool was NOT called!
        var delta = results.OfType<MessageDelta>().FirstOrDefault();
        Assert.NotNull(delta);
        var toolResult = Assert.IsType<ToolResult>(delta.Content);
        Assert.True(toolResult.IsError);
        Assert.Contains("rejected by human-in-the-loop reviewer", toolResult.Contents.OfType<Text>().First().Value);
    }

    [Fact]
    public async Task Test4_DoesNotBlockOrPromptForNonSensitiveTools()
    {
        var promptCount = 0;
        var mock = new MockToolbox(calls => CreateEvents(
            new MessageStart(Role.Tool, Id: calls[0].Id),
            new MessageDelta(calls[0].Id, Content: new ToolResult(calls[0].Id, [new Text("Sunny, 72F")])),
            new MessageEnd(Id: calls[0].Id)
        ));

        var layer = new ApprovalLayer(
            approver: (_, _) =>
            {
                promptCount++;
                return Task.FromResult(true);
            },
            isSensitive: call => call.Name.StartsWith("admin_"),
            inner: mock);

        var calls = new List<ToolCall> { new("c1", "get_weather", "{\"city\": \"Seattle\"}") };
        var results = new List<IMessageEvent>();

        await foreach (var evt in layer.ExecuteAsync(calls))
        {
            results.Add(evt);
        }

        Assert.Equal(0, promptCount); // Approver was never prompted
        Assert.Equal(1, mock.CallCount);
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
