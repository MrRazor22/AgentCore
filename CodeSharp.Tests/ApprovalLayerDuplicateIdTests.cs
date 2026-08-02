using System.Reflection;
using System.Text.Json.Nodes;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using CodeSharp.Layers;
using Xunit;

namespace CodeSharp.Tests;

public class ApprovalLayerDuplicateIdTests
{
    private class DummyTool : Tool
    {
        public DummyTool(string name) : base(name, "Dummy Description", new JsonSchemaBuilder().Type<object>().Build()) { }

        public override Task<object?> InvokeAsync(JsonObject arguments, CancellationToken ct)
            => Task.FromResult<object?>($"Output for {Name}");
    }

    private class MockTooling : ITooling
    {
        private readonly Tool _tool;
        public MockTooling(Tool tool) => _tool = tool;
        public IReadOnlyList<Tool> Tools => new[] { _tool };
        public Task<IReadOnlyList<ToolResult>> ExecuteAsync(IEnumerable<ToolCall> calls, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<ToolResult>>(
                calls.Select(c => new ToolResult(c.Id, new Text($"Output for {c.Name}"))).ToList()
            );
        }
    }

    private class AlwaysApprovePrompt : IApprovalPrompt
    {
        public Task<bool> RequestApprovalAsync(ToolCall call, CancellationToken ct) => Task.FromResult(true);
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateOrEmptyCallIds_PreservesOrderWithoutException()
    {
        var tool = new DummyTool("test_tool");
        var permissions = new Dictionary<string, ToolPermission>
        {
            ["test_tool"] = ToolPermission.Allow
        };

        var approvalLayer = new ApprovalLayer(permissions, ExecutionPolicy.AlwaysAllow, new AlwaysApprovePrompt());
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

        var results = await approvalLayer.ExecuteAsync(calls);

        Assert.Equal(4, results.Count);
        Assert.Equal("", results[0].CallId);
        Assert.Equal("", results[1].CallId);
        Assert.Equal("1", results[2].CallId);
        Assert.Equal("1", results[3].CallId);
    }
}
