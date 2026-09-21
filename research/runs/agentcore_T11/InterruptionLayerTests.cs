using System.Text.Json.Nodes;
using AgentCore;
using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using AgentCore.Tool.Tools;
using Xunit;

namespace AgentCoreT11;

public class InterruptionLayerTests
{
    private sealed class MockSensitiveTool : ITool
    {
        public ToolDefinition Definition { get; } = new(
            "transfer_funds",
            "Transfers funds to recipient",
            new JsonSchemaBuilder().Type<object>().Build());

        public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
            JsonObject arguments,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new Text("Funds transferred successfully: $500.");
        }
    }

    [Fact]
    public async Task Test1_SignalsInterruptionConditionWithoutCorruptingAgentState()
    {
        var store = new InMemoryInterruptionStore();
        var tool = new MockSensitiveTool();
        var innerToolbox = new Toolbox([tool]);

        var layer = new InterruptionLayer(
            shouldInterruptTools: calls => calls.Any(c => c.Name == "transfer_funds"),
            store: store,
            sessionId: "s1",
            inner: innerToolbox);

        var calls = new List<ToolCall> { new("call_1", "transfer_funds", "{\"amount\":500}") };

        var ex = await Assert.ThrowsAsync<AgentInterruptedException>(async () =>
        {
            await foreach (var _ in layer.ExecuteAsync(calls)) { }
        });

        Assert.True(layer.IsInterrupted);
        Assert.NotNull(ex.Snapshot);
        Assert.Equal("tool_approval", ex.Snapshot.InterruptionType);
        Assert.Single(ex.Snapshot.PendingToolCalls!);
        Assert.Equal("transfer_funds", ex.Snapshot.PendingToolCalls![0].Name);
    }

    [Fact]
    public async Task Test2_SerializesExecutionSnapshotAtPointOfInterruption()
    {
        var store = new InMemoryInterruptionStore();
        var tool = new MockSensitiveTool();
        var innerToolbox = new Toolbox([tool]);

        var layer = new InterruptionLayer(
            shouldInterruptTools: calls => true,
            store: store,
            sessionId: "sess_check",
            inner: innerToolbox);

        var calls = new List<ToolCall> { new("c1", "transfer_funds", "{}") };

        try
        {
            await foreach (var _ in layer.ExecuteAsync(calls)) { }
        }
        catch (AgentInterruptedException) { }

        var loadedSnapshot = await store.LoadSnapshotAsync("sess_check");
        Assert.NotNull(loadedSnapshot);
        Assert.Equal("tool_approval", loadedSnapshot.InterruptionType);
        Assert.Single(loadedSnapshot.PendingToolCalls!);
        Assert.Equal("Requires external approval", loadedSnapshot.Metadata?["reason"]?.ToString());
    }

    [Fact]
    public async Task Test3_ResumesExecutionFromSnapshotUponExternalResumeTrigger()
    {
        var store = new InMemoryInterruptionStore();
        var tool = new MockSensitiveTool();
        var innerToolbox = new Toolbox([tool]);

        var layer = new InterruptionLayer(
            shouldInterruptTools: calls => true,
            store: store,
            sessionId: "sess_resume",
            inner: innerToolbox);

        var calls = new List<ToolCall> { new("c1", "transfer_funds", "{}") };

        try
        {
            await foreach (var _ in layer.ExecuteAsync(calls)) { }
        }
        catch (AgentInterruptedException) { }

        Assert.True(layer.IsInterrupted);

        // Resume using external trigger
        var resumeResults = await layer.ResumeAsync("sess_resume", new JsonObject { ["approved"] = true });

        Assert.False(layer.IsInterrupted);
        Assert.NotEmpty(resumeResults);

        // Store snapshot should now be cleared
        var cleared = await store.LoadSnapshotAsync("sess_resume");
        Assert.Null(cleared);
    }

    [Fact]
    public async Task Test4_CompletesRemainderOfTaskCorrectlyUponResumption()
    {
        var store = new InMemoryInterruptionStore();
        var tool = new MockSensitiveTool();
        var innerToolbox = new Toolbox([tool]);

        var layer = new InterruptionLayer(
            shouldInterruptTools: calls => true,
            store: store,
            sessionId: "sess_task",
            inner: innerToolbox);

        var calls = new List<ToolCall> { new("c1", "transfer_funds", "{}") };

        // Attempt 1 interrupts
        await Assert.ThrowsAsync<AgentInterruptedException>(async () =>
        {
            await foreach (var _ in layer.ExecuteAsync(calls)) { }
        });

        // External approval and resumption
        var executedEvents = await layer.ResumeAsync("sess_task");

        // Tool result was emitted and contains success payload
        var deltas = executedEvents.OfType<MessageDelta>().ToList();
        Assert.NotEmpty(deltas);
        var trEnd = deltas.Select(d => d.Content).OfType<ToolResultEnd>().FirstOrDefault();
        Assert.NotNull(trEnd);
        Assert.False(trEnd.IsError);
    }
}
