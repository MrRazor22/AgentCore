using AgentCore.Context.Components;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tool;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace AgentCore.Tests;

internal record ToolExecutionResult(string CallId, IReadOnlyList<IContent> Contents)
{
    public override string ToString() => string.Join("\n", Contents.Select(c => c.ToString()));
}

internal static class ToolingTestExtensions
{
    public static async Task<ToolExecutionResult> ExecuteAsync(this ITooling tooling, ToolCall call, CancellationToken ct = default)
    {
        var msg = await tooling.ExecuteAsync([call], ct).ToMessageAsync(ct: ct);
        return new ToolExecutionResult(msg.Metadata.Get<ToolCallId>()?.Value ?? msg.Id ?? call.Id, msg.Contents);
    }
}

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

        var toolResult = await tooling.ExecuteAsync(toolCall);

        Assert.Equal("call_1", toolResult.CallId);
        Assert.Equal("25", toolResult.ToString());
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

        var toolResult = await tooling.ExecuteAsync(toolCall);

        var resultText = toolResult.ToString();
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
        var toolResult = await tooling.ExecuteAsync(toolCall);

        Assert.Equal("25", toolResult.ToString());
    }

    private class NullNameTool(string name) : ITool
    {
        public ToolDefinition Definition { get; } = !string.IsNullOrWhiteSpace(name)
            ? new(name, "desc", new LLM.Schema.JsonSchemaBuilder().Type<object>().Build())
            : throw new ArgumentException("Name cannot be null or whitespace", nameof(name));

        public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
            JsonObject arguments,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield break;
        }
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
