using System;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;
using AgentCore.Tools;
using AgentCore.LLM.Chat;

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

        registry.Add(tool);

        Assert.Single(registry.Tools);
        Assert.Same(tool, registry.TryGet(tool.Name));
    }

    [Fact]
    public async Task ToolingService_ExecuteAsync_InvokesMethodAndReturnsResult()
    {
        var registry = new ToolRegistry();
        var method = typeof(SampleTools).GetMethod(nameof(SampleTools.Add))!;
        var tool = new MethodTool(method, new SampleTools());
        registry.Add(tool);

        var tooling = new ToolService(registry);

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
        registry.Add(tool);

        var tooling = new ToolService(registry);

        // Missing parameter "b" which is required
        var args = new JsonObject { ["a"] = 10 };
        var toolCall = new ToolCall("call_1", tool.Name, args);

        var results = await tooling.ExecuteAsync(new[] { toolCall });

        Assert.Single(results);
        var resultText = results[0].Contents[0].ForLlm();
        Assert.Contains("Error calling tool", resultText);
    }

    [Fact]
    public void ToolRegistry_ThrowsOnDuplicateName()
    {
        var registry = new ToolRegistry();
        var method = typeof(SampleAddTool).GetMethod(nameof(SampleAddTool.Add))!;
        var tool1 = new MethodTool(method, new SampleAddTool(), name: "calculator");
        var tool2 = new MethodTool(method, new SampleAddTool(), name: "calculator");

        registry.Add(tool1);
        Assert.Throws<InvalidOperationException>(() => registry.Add(tool2));
    }

    [Fact]
    public void ToolRegistry_SupportsCaseInsensitiveLookup()
    {
        var registry = new ToolRegistry();
        var method = typeof(SampleAddTool).GetMethod(nameof(SampleAddTool.Add))!;
        var tool = new MethodTool(method, new SampleAddTool(), name: "weather_lookup");
        registry.Add(tool);

        var retrieved = registry.TryGet("Weather_Lookup");
        Assert.Same(tool, retrieved);
    }

    [Fact]
    public void ToolRegistry_TryGet_EmptyName_ThrowsArgumentException()
    {
        var registry = new ToolRegistry();
        Assert.Throws<ArgumentException>(() => registry.TryGet(""));
    }

    private class NullNameTool : Tool
    {
        public NullNameTool(string name) : base(name, "desc", new LLM.Schema.JsonSchemaBuilder().Type<object>().Build()) { }
        public override Task<object?> InvokeAsync(JsonObject arguments, CancellationToken ct) => Task.FromResult<object?>(null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Tool_Constructor_GuardsAgainstNullOrEmptyNames(string? invalidName)
    {
        Assert.ThrowsAny<ArgumentException>(() => new NullNameTool(invalidName!));
    }

    public class SampleAddTool
    {
        public int Add(int a, int b) => a + b;
    }
}
