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
        public bool ExecuteCalled { get; private set; }
        public IReadOnlyList<ToolDefinition> GetDefinitions() => Array.Empty<ToolDefinition>();

        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
        {
            ExecuteCalled = true;
            return Task.FromResult(new ToolResult(call.Id, [new Text("Execution Ok")]));
        }
    }

    private static void AttachInner(ToolApprovalLayer layer, ITooling inner)
    {
        var method = typeof(ToolingLayer).GetMethod("Attach", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        method!.Invoke(layer, new object[] { inner });
    }

    [Fact]
    public async Task ExecuteAsync_ApprovedEvaluator_ExecutesInner()
    {
        var mockInner = new MockTooling();
        var layer = new ToolApprovalLayer((call, ct) => Task.FromResult<IContent?>(null));
        AttachInner(layer, mockInner);

        var call = new ToolCall("1", "test_tool", new JsonObject());
        var result = await layer.ExecuteAsync(call);

        Assert.True(mockInner.ExecuteCalled);
        Assert.Equal("Execution Ok", result.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_DeniedEvaluator_EmitsDeniedResultAndSkipsInner()
    {
        var mockInner = new MockTooling();
        var layer = new ToolApprovalLayer((call, ct) => Task.FromResult<IContent?>(new Text("[DENIED] User rejected execution.")));
        AttachInner(layer, mockInner);

        var call = new ToolCall("1", "test_tool", new JsonObject());
        var result = await layer.ExecuteAsync(call);

        Assert.False(mockInner.ExecuteCalled);
        var text = result.ToString();
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
        var layer = new ToolApprovalLayer((call, ct) => Task.FromResult<IReadOnlyList<IContent>?>(denialItems));
        AttachInner(layer, mockInner);

        var call = new ToolCall("1", "test_tool", new JsonObject());
        var result = await layer.ExecuteAsync(call);

        Assert.False(mockInner.ExecuteCalled);
        Assert.Equal(2, result.Contents.Count);
        Assert.Equal("1", result.CallId);
        Assert.IsType<Text>(result.Contents[0]);
        Assert.IsType<Reasoning>(result.Contents[1]);
    }

    [Fact]
    public async Task ExecuteAsync_PromptDelegateOverload_Works()
    {
        var mockInner = new MockTooling();
        var layer = new ToolApprovalLayer((call, ct) => Task.FromResult(true));
        AttachInner(layer, mockInner);

        var call = new ToolCall("1", "test_tool", new JsonObject());
        var result = await layer.ExecuteAsync(call);

        Assert.True(mockInner.ExecuteCalled);
        Assert.Equal("Execution Ok", result.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_PromptDelegateOverload_UserDenies_EmitsToolNameRejection()
    {
        var mockInner = new MockTooling();
        var layer = new ToolApprovalLayer((call, ct) => Task.FromResult(false));
        AttachInner(layer, mockInner);

        var call = new ToolCall("1", "test_tool", new JsonObject());
        var result = await layer.ExecuteAsync(call);

        Assert.False(mockInner.ExecuteCalled);
        Assert.Equal("Execution of tool 'test_tool' was rejected by the user.", result.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_CustomGuardrailEvaluator_BlocksMatchingCalls()
    {
        var mockInner = new MockTooling();
        var layer = new ToolApprovalLayer((call, ct) =>
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
        var result = await layer.ExecuteAsync(call);

        Assert.False(mockInner.ExecuteCalled);
        Assert.Contains("[DENIED] Blocked by guardrail.", result.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_CancellationPropagation_ThrowsOperationCanceledException()
    {
        var mockInner = new MockTooling();
        var layer = new ToolApprovalLayer(async (call, ct) =>
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
        });
        
        Assert.False(mockInner.ExecuteCalled);
    }

    private class TimedMockTooling : ITooling
    {
        public IReadOnlyList<ToolDefinition> GetDefinitions() => Array.Empty<ToolDefinition>();

        public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
        {
            var delay = call.Name == "tool_b" ? 40 : 10;
            await Task.Delay(delay, ct);
            return new ToolResult(call.Id, [new Text($"Result for {call.Name}")]);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentCalls_FastDenialAndFastToolStreamBeforeSlowApproval()
    {
        var mockInner = new TimedMockTooling();

        var layer = new ToolApprovalLayer(async (call, ct) =>
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

        var tasks = new List<Task<ToolResult>>
        {
            layer.ExecuteAsync(callA),
            layer.ExecuteAsync(callB),
            layer.ExecuteAsync(callC)
        };

        var received = new List<ToolResult>();
        while (tasks.Count > 0)
        {
            var completed = await Task.WhenAny(tasks);
            tasks.Remove(completed);
            received.Add(await completed);
        }

        Assert.Equal(3, received.Count);

        // Tool C (fast denial ~10ms) and Tool B (fast inner tool ~40ms) must complete before Tool A (~250ms)
        var callIds = received.Select(r => r.CallId).ToList();
        Assert.Equal("call_A", callIds[2]); // Tool A is last
        Assert.Contains("call_B", callIds.Take(2));
        Assert.Contains("call_C", callIds.Take(2));
    }
}
