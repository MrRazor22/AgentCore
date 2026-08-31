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
        private readonly List<Task<ToolResult>> _tasks = new();
        public MockTooling(Tool tool) => _tool = tool;
        public IReadOnlyList<ToolDefinition> GetDefinitions() => new[] { _tool.Definition };
        public Task ExecuteAsync(ToolCall call, CancellationToken ct = default)
        {
            _tasks.Add(Task.FromResult(new ToolResult(call.Id, new Text($"Output for {call.Name}"))));
            return Task.CompletedTask;
        }
        public async IAsyncEnumerable<ToolResult> StreamResultsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var tasks = new List<Task<ToolResult>>(_tasks);
            _tasks.Clear();
            foreach (var t in tasks)
            {
                yield return await t.ConfigureAwait(false);
            }
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

        foreach (var call in calls)
        {
            await approvalLayer.ExecuteAsync(call);
        }

        var results = new List<ToolResult>();
        await foreach (var r in approvalLayer.StreamResultsAsync())
        {
            results.Add(r);
        }

        Assert.Equal(4, results.Count);
        Assert.Equal("", results[0].CallId);
        Assert.Equal("", results[1].CallId);
        Assert.Equal("1", results[2].CallId);
        Assert.Equal("1", results[3].CallId);
    }
}
