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
    public async Task ExecuteAsync_PromptDelegateOverload_UserDenies_EmitsToolNameRejection()
    {
        var mockInner = new MockTooling();
        var layer = new ApprovalLayer((call, ct) => Task.FromResult(false));
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
        Assert.Equal("Execution of tool 'test_tool' was rejected by the user.", Assert.IsType<Text>(results[0].Result).Value);
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

    private class TimedMockTooling : ITooling
    {
        private readonly List<Task<ToolResult>> _tasks = [];
        public IReadOnlyList<ToolDefinition> GetDefinitions() => Array.Empty<ToolDefinition>();

        public Task ExecuteAsync(ToolCall call, CancellationToken ct = default)
        {
            var task = Task.Run(async () =>
            {
                var delay = call.Name == "tool_b" ? 40 : 10;
                await Task.Delay(delay, ct);
                return new ToolResult(call.Id, new Text($"Result for {call.Name}"));
            }, ct);
            _tasks.Add(task);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<ToolResult> StreamResultsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var tasks = new List<Task<ToolResult>>(_tasks);
            _tasks.Clear();
            while (tasks.Count > 0)
            {
                var completed = await Task.WhenAny(tasks).ConfigureAwait(false);
                tasks.Remove(completed);
                yield return await completed.ConfigureAwait(false);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentCalls_FastDenialAndFastToolStreamBeforeSlowApproval()
    {
        var mockInner = new TimedMockTooling();

        var layer = new ApprovalLayer(async (call, ct) =>
        {
            if (call.Name == "tool_a")
            {
                // Slow approval (e.g. 250ms user prompt)
                await Task.Delay(250, ct);
                return null;
            }
            if (call.Name == "tool_b")
            {
                // Instant approval -> inner runs in 40ms
                return null;
            }
            if (call.Name == "tool_c")
            {
                // Fast denial (10ms)
                await Task.Delay(10, ct);
                return [new Text("Denied tool_c")];
            }
            return null;
        });
        AttachInner(layer, mockInner);

        var callA = new ToolCall("call_A", "tool_a", new JsonObject());
        var callB = new ToolCall("call_B", "tool_b", new JsonObject());
        var callC = new ToolCall("call_C", "tool_c", new JsonObject());

        await layer.ExecuteAsync(callA);
        await layer.ExecuteAsync(callB);
        await layer.ExecuteAsync(callC);

        var received = new List<ToolResult>();
        await foreach (var r in layer.StreamResultsAsync())
        {
            received.Add(r);
        }

        Assert.Equal(3, received.Count);

        // Tool C (fast denial ~10ms) and Tool B (fast inner tool ~40ms) must stream before Tool A (~250ms)
        var callIds = received.Select(r => r.CallId).ToList();
        Assert.Equal("call_A", callIds[2]); // Tool A is last
        Assert.Contains("call_B", callIds.Take(2));
        Assert.Contains("call_C", callIds.Take(2));
    }
}
