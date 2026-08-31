using AgentCore.LLM.Chat;
using AgentCore.Tools;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace AgentCore.Tests;

public class ToolingTests
{
    public class SampleTools
    {
        [Description("Add two integers")]
        public int Add(int a, int b) => a + b;
    }

    [Fact]
    public async Task ToolingService_ExecuteAsync_InvokesMethodAndReturnsResult()
    {
        var method = typeof(SampleTools).GetMethod(nameof(SampleTools.Add))!;
        var tool = new MethodTool(method, new SampleTools());

        var tooling = new Tooling(new[] { tool });

        var args = new JsonObject { ["a"] = 10, ["b"] = 15 };
        var toolCall = new ToolCall("call_1", tool.Definition.Name, args);

        await tooling.ExecuteAsync(toolCall);
        var results = new List<ToolResult>();
        await foreach (var r in tooling.StreamResultsAsync())
        {
            results.Add(r);
        }

        Assert.Single(results);
        var toolResult = results[0];
        Assert.Equal("call_1", toolResult.CallId);
        Assert.Equal("25", toolResult.Result!.ToString());
    }

    [Fact]
    public async Task ToolingService_ExecuteAsync_ValidationError_ReturnsErrorMessage()
    {
        var method = typeof(SampleTools).GetMethod(nameof(SampleTools.Add))!;
        var tool = new MethodTool(method, new SampleTools());

        var tooling = new Tooling(new[] { tool });

        // Missing parameter "b" which is required
        var args = new JsonObject { ["a"] = 10 };
        var toolCall = new ToolCall("call_1", tool.Definition.Name, args);

        await tooling.ExecuteAsync(toolCall);
        var results = new List<ToolResult>();
        await foreach (var r in tooling.StreamResultsAsync())
        {
            results.Add(r);
        }

        Assert.Single(results);
        var resultText = results[0].ToString();
        Assert.Contains("Error calling tool", resultText);
    }

    [Fact]
    public void Builder_ThrowsOnDuplicateName()
    {
        var builder = Agent.Create()
            .WithTools(new SampleAddTool())
            .WithTools(new SampleAddTool())
            .WithLLM(lf => new MockLLMProvider()); // Needs LLM to build

        Assert.Throws<ArgumentException>(() => builder.Build());
    }

    [Fact]
    public async Task ToolService_SupportsCaseInsensitiveLookup()
    {
        var method = typeof(SampleAddTool).GetMethod(nameof(SampleAddTool.Add))!;
        var tool = new MethodTool(method, new SampleAddTool(), name: "weather_lookup");
        var tooling = new Tooling(new[] { tool });

        var args = new JsonObject { ["a"] = 10, ["b"] = 15 };
        var toolCall = new ToolCall("call_1", "Weather_Lookup", args);
        await tooling.ExecuteAsync(toolCall);
        var results = new List<ToolResult>();
        await foreach (var r in tooling.StreamResultsAsync())
        {
            results.Add(r);
        }

        Assert.Single(results);
        var toolResult = results[0];
        Assert.Equal("25", toolResult.Result!.ToString());
    }

    private class NullNameTool : Tool
    {
        public NullNameTool(string name) : base(new ToolDefinition(name, "desc", new LLM.Schema.JsonSchemaBuilder().Type<object>().Build())) { }
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
        [Tool]
        public int Add(int a, int b) => a + b;
    }
}
