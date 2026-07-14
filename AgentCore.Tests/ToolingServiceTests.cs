using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AgentCore.Conversation;
using AgentCore.Tools;
using AgentCore.LLM.Schema;

namespace AgentCore.Tests;

public class ToolingServiceTests
{
    private class FakeTool : Tool
    {
        public Func<JsonObject, CancellationToken, Task<object?>> Invoker { get; set; } = 
            (args, ct) => Task.FromResult<object?>("Result");

        public FakeTool(string name, JsonSchema schema) : base(name, "Fake Description", schema) { }

        public override Task<object?> InvokeAsync(JsonObject arguments, CancellationToken ct) => Invoker(arguments, ct);
    }

    [Fact]
    public async Task ExecuteAsync_UnregisteredToolName_ReturnsErrorMessage()
    {
        var registry = new ToolRegistry();
        var tooling = new ToolService(registry);
        var calls = new[] { new ToolCall("call_1", "missing_tool", new JsonObject()) };

        var results = await tooling.ExecuteAsync(calls);

        Assert.Single(results);
        var resultText = results[0].Contents[0].ForLlm();
        Assert.Contains("not registered", resultText);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyToolName_ReturnsErrorMessage()
    {
        var registry = new ToolRegistry();
        var tooling = new ToolService(registry);
        var calls = new[] { new ToolCall("call_1", "", new JsonObject()) };

        var results = await tooling.ExecuteAsync(calls);

        Assert.Single(results);
        var resultText = results[0].Contents[0].ForLlm();
        Assert.Contains("cannot be empty", resultText);
    }

    [Fact]
    public async Task ExecuteAsync_ToolThrowsException_ReturnsErrorMessageNotException()
    {
        var registry = new ToolRegistry();
        var schema = new Schema.JsonSchemaBuilder().Type<object>().Build();
        var tool = new FakeTool("crash_tool", schema)
        {
            Invoker = (args, ct) => throw new InvalidOperationException("Tool implementation crashed")
        };
        registry.Add(tool);
        var tooling = new ToolService(registry);

        var calls = new[] { new ToolCall("call_1", "crash_tool", new JsonObject()) };

        // Should NOT throw, but return error text
        var results = await tooling.ExecuteAsync(calls);

        Assert.Single(results);
        var resultText = results[0].Contents[0].ForLlm();
        Assert.Contains("Tool implementation crashed", resultText);
    }

    [Fact]
    public async Task ExecuteAsync_ToolReturnsNull_ReturnsEmptyText()
    {
        var registry = new ToolRegistry();
        var schema = new Schema.JsonSchemaBuilder().Type<object>().Build();
        var tool = new FakeTool("null_tool", schema) { Invoker = (args, ct) => Task.FromResult<object?>(null) };
        registry.Add(tool);
        var tooling = new ToolService(registry);

        var calls = new[] { new ToolCall("call_1", "null_tool", new JsonObject()) };
        var results = await tooling.ExecuteAsync(calls);

        Assert.Single(results);
        var resultText = results[0].Contents[0].ForLlm();
        Assert.Equal("", resultText);
    }

    [Fact]
    public async Task ExecuteAsync_ToolReturnsIContent_UsedDirectly()
    {
        var registry = new ToolRegistry();
        var schema = new Schema.JsonSchemaBuilder().Type<object>().Build();
        var tool = new FakeTool("content_tool", schema) 
        { 
            Invoker = (args, ct) => Task.FromResult<object?>(new Text("Explicit IContent")) 
        };
        registry.Add(tool);
        var tooling = new ToolService(registry);

        var calls = new[] { new ToolCall("call_1", "content_tool", new JsonObject()) };
        var results = await tooling.ExecuteAsync(calls);

        Assert.Single(results);
        var resultText = results[0].Contents[0].ForLlm();
        Assert.Equal("Explicit IContent", resultText);
    }

    [Fact]
    public async Task ExecuteAsync_ToolReturnsObject_JsonSerialized()
    {
        var registry = new ToolRegistry();
        var schema = new Schema.JsonSchemaBuilder().Type<object>().Build();
        var tool = new FakeTool("object_tool", schema) 
        { 
            Invoker = (args, ct) => Task.FromResult<object?>(new { Key = "Val" }) 
        };
        registry.Add(tool);
        var tooling = new ToolService(registry);

        var calls = new[] { new ToolCall("call_1", "object_tool", new JsonObject()) };
        var results = await tooling.ExecuteAsync(calls);

        Assert.Single(results);
        var resultText = results[0].Contents[0].ForLlm();
        Assert.Contains("{\"Key\":\"Val\"}", resultText);
    }

    [Fact]
    public async Task ExecuteAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        var registry = new ToolRegistry();
        var schema = new Schema.JsonSchemaBuilder().Type<object>().Build();
        var tool = new FakeTool("slow_tool", schema)
        {
            Invoker = async (args, ct) => { await Task.Delay(5000, ct); return "Done"; }
        };
        registry.Add(tool);
        var tooling = new ToolService(registry);

        var calls = new[] { new ToolCall("call_1", "slow_tool", new JsonObject()) };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await tooling.ExecuteAsync(calls, cts.Token);
        });
    }
}
