using System;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;
using AgentCore.Conversation;
using AgentCore.Tooling;

namespace AgentCore.Tests;

public class ToolingTests
{
    public class SampleTools
    {
        [Description("Add two integers")]
        public int Add(int a, int b) => a + b;
    }

    [Fact]
    public void ToolRegistry_RegisterAndRetrieve_Succeeds()
    {
        var registry = new ToolRegistry();
        var method = typeof(SampleTools).GetMethod(nameof(SampleTools.Add))!;
        var tool = new MethodTool(method, new SampleTools());

        registry.Register(tool);

        Assert.Single(registry.Tools);
        Assert.Same(tool, registry.TryGet(tool.Name));
    }

    [Fact]
    public async Task ToolingService_ExecuteAsync_InvokesMethodAndReturnsResult()
    {
        var registry = new ToolRegistry();
        var method = typeof(SampleTools).GetMethod(nameof(SampleTools.Add))!;
        var tool = new MethodTool(method, new SampleTools());
        registry.Register(tool);

        var tooling = new ToolingService(registry);

        var args = new JsonObject { ["a"] = 10, ["b"] = 15 };
        var toolCall = new ToolCall("call_1", tool.Name, args);

        var results = await tooling.ExecuteAsync(new[] { toolCall });

        Assert.Single(results);
        var message = results[0];
        Assert.Equal(Role.Tool, message.Role);
        var toolResult = (ToolResult)message.Contents[0];
        Assert.Equal("call_1", toolResult.CallId);
        Assert.Equal("25", toolResult.Result!.ForLlm());
    }

    [Fact]
    public async Task ToolingService_ExecuteAsync_ValidationError_ReturnsErrorMessage()
    {
        var registry = new ToolRegistry();
        var method = typeof(SampleTools).GetMethod(nameof(SampleTools.Add))!;
        var tool = new MethodTool(method, new SampleTools());
        registry.Register(tool);

        var tooling = new ToolingService(registry);

        // Missing parameter "b" which is required
        var args = new JsonObject { ["a"] = 10 };
        var toolCall = new ToolCall("call_1", tool.Name, args);

        var results = await tooling.ExecuteAsync(new[] { toolCall });

        Assert.Single(results);
        var resultText = results[0].Contents[0].ForLlm();
        Assert.Contains("Error calling tool", resultText);
    }
}
