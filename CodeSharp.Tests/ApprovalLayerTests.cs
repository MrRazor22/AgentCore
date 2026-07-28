using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using CodeSharp.Layers;
using Xunit;

namespace CodeSharp.Tests;

public class ApprovalLayerTests
{
    private class TestTool : Tool
    {
        public TestTool(string name) : base(name, "Test tool", new JsonSchemaBuilder().Type<object>().Build()) { }
        public override Task<object?> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.FromResult<object?>("ok");
    }

    private class MockTooling : ITooling
    {
        public bool ExecuteCalled { get; private set; }
        public IReadOnlyList<Tool> Tools => Array.Empty<Tool>();

        public Task<IReadOnlyList<ToolResult>> ExecuteAsync(IEnumerable<ToolCall> calls, CancellationToken ct = default)
        {
            ExecuteCalled = true;
            return Task.FromResult<IReadOnlyList<ToolResult>>(
                calls.Select(c => new ToolResult(c.Id, new Text("Execution Ok"))).ToList()
            );
        }
    }

    private class ConfigurablePrompt : IApprovalPrompt
    {
        private readonly bool _approve;
        private readonly Exception? _exceptionToThrow;

        public ConfigurablePrompt(bool approve, Exception? exceptionToThrow = null)
        {
            _approve = approve;
            _exceptionToThrow = exceptionToThrow;
        }

        public Task<bool> RequestApprovalAsync(ToolCall call, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            if (_exceptionToThrow != null)
            {
                throw _exceptionToThrow;
            }
            return Task.FromResult(_approve);
        }
    }

    private static void AttachInner(ApprovalLayer layer, ITooling inner)
    {
        var method = typeof(ToolingLayer).GetMethod("Attach", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        method!.Invoke(layer, new object[] { inner });
    }

    [Fact]
    public async Task ExecuteAsync_StrictPolicy_AlwaysPromptsAndApproves()
    {
        var mockInner = new MockTooling();
        var prompt = new ConfigurablePrompt(approve: true);
        var permissions = new Dictionary<string, ToolPermission> { ["test_tool"] = ToolPermission.Confirm };
        var layer = new ApprovalLayer(permissions, ExecutionPolicy.Strict, prompt);
        AttachInner(layer, mockInner);

        var calls = new[] { new ToolCall("1", "test_tool", new JsonObject()) };
        var results = await layer.ExecuteAsync(calls);

        Assert.True(mockInner.ExecuteCalled);
        Assert.Single(results);
        Assert.Equal("Execution Ok", ((Text)results[0].Result).Value);
    }

    [Fact]
    public async Task ExecuteAsync_StrictPolicy_UserDenies_ExecutorNotCalled()
    {
        var mockInner = new MockTooling();
        var prompt = new ConfigurablePrompt(approve: false);
        var permissions = new Dictionary<string, ToolPermission> { ["test_tool"] = ToolPermission.Confirm };
        var layer = new ApprovalLayer(permissions, ExecutionPolicy.Strict, prompt);
        AttachInner(layer, mockInner);

        var calls = new[] { new ToolCall("1", "test_tool", new JsonObject()) };
        var results = await layer.ExecuteAsync(calls);

        Assert.False(mockInner.ExecuteCalled);
        Assert.Single(results);
        Assert.Contains("[DENIED]", ((Text)results[0].Result).Value);
    }

    [Fact]
    public async Task ExecuteAsync_TrustModelPolicy_AutoApprovesSafeToAutoRun()
    {
        var mockInner = new MockTooling();
        var prompt = new ConfigurablePrompt(approve: false); // Prompt would deny if called
        var permissions = new Dictionary<string, ToolPermission> { ["test_tool"] = ToolPermission.Confirm };
        var layer = new ApprovalLayer(permissions, ExecutionPolicy.TrustModel, prompt);
        AttachInner(layer, mockInner);

        var calls = new[] { new ToolCall("1", "test_tool", new JsonObject { ["SafeToAutoRun"] = true }) };
        var results = await layer.ExecuteAsync(calls);

        Assert.True(mockInner.ExecuteCalled);
        Assert.Single(results);
        Assert.Equal("Execution Ok", ((Text)results[0].Result).Value);
    }

    [Fact]
    public async Task ExecuteAsync_AlwaysAllowPolicy_AutoApprovesWithoutPrompt()
    {
        var mockInner = new MockTooling();
        var prompt = new ConfigurablePrompt(approve: false); // Prompt would deny if called
        var permissions = new Dictionary<string, ToolPermission> { ["test_tool"] = ToolPermission.Confirm };
        var layer = new ApprovalLayer(permissions, ExecutionPolicy.AlwaysAllow, prompt);
        AttachInner(layer, mockInner);

        var calls = new[] { new ToolCall("1", "test_tool", new JsonObject()) };
        var results = await layer.ExecuteAsync(calls);

        Assert.True(mockInner.ExecuteCalled);
        Assert.Single(results);
        Assert.Equal("Execution Ok", ((Text)results[0].Result).Value);
    }

    [Fact]
    public async Task ExecuteAsync_GuardrailDeny_BlocksAndExecutorNotCalled()
    {
        var mockInner = new MockTooling();
        var prompt = new ConfigurablePrompt(approve: true);
        var permissions = new Dictionary<string, ToolPermission> { ["RunCommand"] = ToolPermission.Confirm };
        
        // Add rule to block format c:
        var guardrails = DenyRules.CommandPatterns("format c:");
        var layer = new ApprovalLayer(permissions, ExecutionPolicy.AlwaysAllow, prompt, guardrails);
        AttachInner(layer, mockInner);

        var calls = new[] { new ToolCall("1", "RunCommand", new JsonObject { ["CommandLine"] = "format c: /q" }) };
        var results = await layer.ExecuteAsync(calls);

        Assert.False(mockInner.ExecuteCalled);
        Assert.Single(results);
        Assert.Contains("[DENIED]", ((Text)results[0].Result).Value);
        Assert.Contains("defense-in-depth guardrail", ((Text)results[0].Result).Value);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationPropagation_ThrowsOperationCanceledException()
    {
        var mockInner = new MockTooling();
        var prompt = new ConfigurablePrompt(approve: true);
        var permissions = new Dictionary<string, ToolPermission> { ["test_tool"] = ToolPermission.Confirm };
        var layer = new ApprovalLayer(permissions, ExecutionPolicy.Strict, prompt);
        AttachInner(layer, mockInner);

        var calls = new[] { new ToolCall("1", "test_tool", new JsonObject()) };
        
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await layer.ExecuteAsync(calls, cts.Token);
        });
        
        Assert.False(mockInner.ExecuteCalled);
    }
}
