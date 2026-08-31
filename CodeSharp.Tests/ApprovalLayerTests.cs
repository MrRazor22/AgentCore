using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using Xunit;

namespace CodeSharp.Tests;

public class ApprovalLayerTests
{
    private class TestTool : Tool
    {
        public TestTool(string name) : base(new ToolDefinition(name, "Test tool", new JsonSchemaBuilder().Type<object>().Build())) { }
        public override Task<object?> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.FromResult<object?>("ok");
    }

    private class MockTooling : ITooling
    {
        private readonly List<Task<ToolResult>> _tasks = new();
        public bool ExecuteCalled { get; private set; }
        public IReadOnlyList<ToolDefinition> GetDefinitions() => Array.Empty<ToolDefinition>();

        public Task ExecuteAsync(ToolCall call, CancellationToken ct = default)
        {
            ExecuteCalled = true;
            _tasks.Add(Task.FromResult(new ToolResult(call.Id, new Text("Execution Ok"))));
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

    private static void AttachInner(ApprovalLayer layer, ITooling inner)
    {
        var method = typeof(ToolingLayer).GetMethod("Attach", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        method!.Invoke(layer, new object[] { inner });
    }

    [Fact]
    public async Task ExecuteAsync_ApprovedEvaluator_ExecutesInner()
    {
        var mockInner = new MockTooling();
        var layer = new ApprovalLayer((call, ct) => Task.FromResult<IContent?>(null));
        AttachInner(layer, mockInner);

        var call = new ToolCall("1", "test_tool", new JsonObject());
        await layer.ExecuteAsync(call);
        var results = new List<ToolResult>();
        await foreach (var r in layer.StreamResultsAsync())
        {
            results.Add(r);
        }

        Assert.True(mockInner.ExecuteCalled);
        Assert.Single(results);
        Assert.Equal("Execution Ok", Assert.IsType<Text>(results[0].Result).Value);
    }

    [Fact]
    public async Task ExecuteAsync_DeniedEvaluator_EmitsDeniedResultAndSkipsInner()
    {
        var mockInner = new MockTooling();
        var layer = new ApprovalLayer((call, ct) => Task.FromResult<IContent?>(new Text("[DENIED] User rejected execution.")));
        AttachInner(layer, mockInner);

        var call = new ToolCall("1", "test_tool", new JsonObject());
        await layer.ExecuteAsync(call);
        var results = new List<ToolResult>();
        await foreach (var r in layer.StreamResultsAsync())
        {
            results.Add(r);
        }

        Assert.False(mockInner.ExecuteCalled);
        Assert.Single(results);
        var text = Assert.IsType<Text>(results[0].Result).Value;
        Assert.Contains("[DENIED]", text);
        Assert.Contains("User rejected execution.", text);
    }

    [Fact]
    public async Task ExecuteAsync_MultiContentDenial_EmitsAllContents()
    {
        var mockInner = new MockTooling();
        var denialItems = new IContent[]
        {
            new Text("[DENIED] Layout invalid."),
            new Reasoning("Checked against design specs.")
        };
        var layer = new ApprovalLayer((call, ct) => Task.FromResult<IReadOnlyList<IContent>?>(denialItems));
        AttachInner(layer, mockInner);

        var call = new ToolCall("1", "test_tool", new JsonObject());
        await layer.ExecuteAsync(call);
        var results = new List<ToolResult>();
        await foreach (var r in layer.StreamResultsAsync())
        {
            results.Add(r);
        }

        Assert.False(mockInner.ExecuteCalled);
        Assert.Equal(2, results.Count);
        Assert.Equal("1", results[0].CallId);
        Assert.Equal("1", results[1].CallId);
        Assert.IsType<Text>(results[0].Result);
        Assert.IsType<Reasoning>(results[1].Result);
    }

    [Fact]
    public async Task ExecuteAsync_PromptDelegateOverload_Works()
    {
        var mockInner = new MockTooling();
        var layer = new ApprovalLayer((call, ct) => Task.FromResult(true));
        AttachInner(layer, mockInner);

        var call = new ToolCall("1", "test_tool", new JsonObject());
        await layer.ExecuteAsync(call);
        var results = new List<ToolResult>();
        await foreach (var r in layer.StreamResultsAsync())
        {
            results.Add(r);
        }

        Assert.True(mockInner.ExecuteCalled);
        Assert.Single(results);
        Assert.Equal("Execution Ok", Assert.IsType<Text>(results[0].Result).Value);
    }

    [Fact]
    public async Task ExecuteAsync_CustomGuardrailEvaluator_BlocksMatchingCalls()
    {
        var mockInner = new MockTooling();
        var layer = new ApprovalLayer((call, ct) =>
        {
            if (call.Name == "RunCommand" &&
                call.Arguments.TryGetPropertyValue("CommandLine", out var cmd) &&
                cmd?.GetValue<string>()?.Contains("format c:") == true)
            {
                return Task.FromResult<IContent?>(new Text("[DENIED] Blocked by guardrail."));
            }
            return Task.FromResult<IContent?>(null);
        });
        AttachInner(layer, mockInner);

        var call = new ToolCall("1", "RunCommand", new JsonObject { ["CommandLine"] = "format c: /q" });
        await layer.ExecuteAsync(call);
        var results = new List<ToolResult>();
        await foreach (var r in layer.StreamResultsAsync())
        {
            results.Add(r);
        }

        Assert.False(mockInner.ExecuteCalled);
        Assert.Single(results);
        Assert.Contains("[DENIED] Blocked by guardrail.", Assert.IsType<Text>(results[0].Result).Value);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationPropagation_ThrowsOperationCanceledException()
    {
        var mockInner = new MockTooling();
        var layer = new ApprovalLayer(async (call, ct) =>
        {
            await Task.Delay(100, ct);
            return (IContent?)null;
        });
        AttachInner(layer, mockInner);

        var call = new ToolCall("1", "test_tool", new JsonObject());
        
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await layer.ExecuteAsync(call, cts.Token);
            await foreach (var r in layer.StreamResultsAsync(cts.Token))
            {
            }
        });
        
        Assert.False(mockInner.ExecuteCalled);
    }
}
