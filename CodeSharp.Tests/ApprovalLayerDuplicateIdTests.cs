using System.Reflection;
using System.Text.Json.Nodes;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using Xunit;

namespace CodeSharp.Tests;

public class ApprovalLayerDuplicateIdTests
{
    private class DummyTool : Tool
    {
        public DummyTool(string name) : base(new ToolDefinition(name, "Dummy Description", new JsonSchemaBuilder().Type<object>().Build())) { }

        public override Task<object?> InvokeAsync(JsonObject arguments, CancellationToken ct)
            => Task.FromResult<object?>($"Output for {Definition.Name}");
    }

    private class MockTooling : ITooling
    {
        private readonly Tool _tool;
        public MockTooling(Tool tool) => _tool = tool;
        public IReadOnlyList<ToolDefinition> GetDefinitions() => new[] { _tool.Definition };
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
