using System.Reflection;
using System.Text.Json.Nodes;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tooling;
using Xunit;

namespace CodeSharp.Tests;

public class ApprovalLayerDuplicateIdTests
{
    private class DummyTool(string name) : ITool
    {
        public ToolDefinition Info { get; } = new(name, "Dummy Description", new JsonSchemaBuilder().Type<object>().Build());

        public async IAsyncEnumerable<IAgentEvent> InvokeStreamingAsync(
            string callId,
            JsonObject arguments,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new ToolResult(callId, [new Text($"Output for {Info.Name}")]);
        }
    }

    private class MockTooling(ITool tool) : IToolbox
    {
        public IReadOnlyList<ToolDefinition> GetDefinitions() => new[] { tool.Info };

        public async IAsyncEnumerable<IAgentEvent> ExecuteStreamingAsync(
            IReadOnlyList<ToolCall> calls,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var call in calls)
            {
                yield return new ToolResult(call.Id, [new Text($"Output for {call.Name}")]);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateOrEmptyCallIds_PreservesOrderWithoutException()
    {
        var tool = new DummyTool("test_tool");
        var approvalLayer = new ToolApprovalLayer((call, ct) => Task.FromResult<IContent?>(null));
        var mockInner = new MockTooling(tool);

        // Attach inner tooling via internal Attach method using reflection for test isolation
        var attachMethod = typeof(ToolingLayer).GetMethod("Attach", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        attachMethod!.Invoke(approvalLayer, new object[] { mockInner });

        // Tool calls with duplicate and empty IDs ["", "", "1", "1"]
        var calls = new[]
        {
            new ToolCall("", "test_tool"),
            new ToolCall("", "test_tool"),
            new ToolCall("1", "test_tool"),
            new ToolCall("1", "test_tool")
        };

        var results = new List<ToolResult>();
        await foreach (var evt in approvalLayer.ExecuteAsync(calls))
        {
            if (evt is ToolResult tr) results.Add(tr);
        }

        Assert.Equal(4, results.Count);
        Assert.Equal("", results[0].CallId);
        Assert.Equal("", results[1].CallId);
        Assert.Equal("1", results[2].CallId);
        Assert.Equal("1", results[3].CallId);
    }
}
