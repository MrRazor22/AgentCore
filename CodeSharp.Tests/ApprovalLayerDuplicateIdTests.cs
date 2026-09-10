using System.Reflection;
using System.Text.Json.Nodes;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using Xunit;

namespace CodeSharp.Tests;

public class ApprovalLayerDuplicateIdTests
{
    private class DummyTool(string name) : ITool
    {
        public ToolDefinition Definition { get; } = new(name, "Dummy Description", new JsonSchemaBuilder().Type<object>().Build());

        public Task<IReadOnlyList<IContent>> InvokeAsync(JsonObject arguments, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IContent>>([new Text($"Output for {Definition.Name}")]);
    }

    private class MockTooling(ITool tool) : ITooling
    {
        public IReadOnlyList<ToolDefinition> GetDefinitions() => new[] { tool.Definition };

        public async IAsyncEnumerable<ToolResult> ExecuteAsync(
            IReadOnlyList<ToolCall> calls,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var call in calls)
            {
                yield return new ToolResult(call.Id, [new Text($"Output for {call.Name}")]);
            }
        }

        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
        {
            return Task.FromResult(new ToolResult(call.Id, [new Text($"Output for {call.Name}")]));
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
            new ToolCall("", "test_tool", new JsonObject()),
            new ToolCall("", "test_tool", new JsonObject()),
            new ToolCall("1", "test_tool", new JsonObject()),
            new ToolCall("1", "test_tool", new JsonObject())
        };

        var results = new List<ToolResult>();
        foreach (var call in calls)
        {
            results.Add(await approvalLayer.ExecuteAsync(call));
        }

        Assert.Equal(4, results.Count);
        Assert.Equal("", results[0].CallId);
        Assert.Equal("", results[1].CallId);
        Assert.Equal("1", results[2].CallId);
        Assert.Equal("1", results[3].CallId);
    }
}
