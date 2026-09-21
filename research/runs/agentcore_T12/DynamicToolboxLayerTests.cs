using System.Text.Json.Nodes;
using AgentCore;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using Xunit;

namespace AgentCoreT12;

public class DynamicToolboxLayerTests
{
    private sealed class SimpleTool(string name, string desc) : ITool
    {
        public ToolDefinition Definition { get; } = new(
            name,
            desc,
            new JsonSchemaBuilder().Type<object>().Build());

        public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
            JsonObject arguments,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new Text($"Result from {Definition.Name}");
        }
    }

    [Fact]
    public async Task Test1_FiltersExposedToolDefinitionsBasedOnCallerContext()
    {
        var publicTool = new SimpleTool("public_search", "Search docs");
        var adminTool = new SimpleTool("admin_delete", "Delete item");

        var layer = new DynamicToolboxLayer();
        layer.RegisterTool(publicTool, allowedRoles: new[] { "guest", "admin" });
        layer.RegisterTool(adminTool, allowedRoles: new[] { "admin" });

        var guestTools = await layer.GetToolsAsync(role: "guest");
        Assert.Single(guestTools);
        Assert.Equal("public_search", guestTools[0].Definition.Name);

        var adminTools = await layer.GetToolsAsync(role: "admin");
        Assert.Equal(2, adminTools.Count);
        Assert.Contains(adminTools, t => t.Definition.Name == "public_search");
        Assert.Contains(adminTools, t => t.Definition.Name == "admin_delete");
    }

    [Fact]
    public async Task Test2_DispatchesInvocationOnlyToCurrentlyActiveTools()
    {
        var tool = new SimpleTool("calculator", "Calculates math");
        var layer = new DynamicToolboxLayer();
        layer.RegisterTool(tool, isActive: true);

        var calls = new List<ToolCall> { new("call_1", "calculator", "{}") };
        var results = new List<IMessageEvent>();

        await foreach (var evt in layer.ExecuteAsync(calls))
        {
            results.Add(evt);
        }

        var delta = results.OfType<MessageDelta>().FirstOrDefault(d => d.Content is ToolResult);
        Assert.NotNull(delta);
        var tr = Assert.IsType<ToolResult>(delta.Content);
        Assert.Equal("call_1", tr.ToolCallId);
        Assert.Contains("Result from calculator", tr.Contents.OfType<Text>().First().Value);
    }

    [Fact]
    public async Task Test3_RejectsInvocationOfDeactivatedToolsWithInformativeError()
    {
        var tool = new SimpleTool("sensitive_export", "Exports sensitive database");
        var layer = new DynamicToolboxLayer();
        layer.RegisterTool(tool, isActive: true);

        layer.SetToolActive("sensitive_export", false);

        var calls = new List<ToolCall> { new("call_2", "sensitive_export", "{}") };
        var results = new List<IMessageEvent>();

        await foreach (var evt in layer.ExecuteAsync(calls))
        {
            results.Add(evt);
        }

        var delta = results.OfType<MessageDelta>().FirstOrDefault(d => d.Content is ToolResult);
        Assert.NotNull(delta);
        var tr = Assert.IsType<ToolResult>(delta.Content);
        Assert.Equal("call_2", tr.ToolCallId);
        var text = tr.Contents.OfType<Text>().First().Value;
        Assert.Contains("Error: Tool 'sensitive_export' is currently deactivated", text);
    }

    [Fact]
    public async Task Test4_UpdatesToolSchemaPresentedToModelOnSubsequentTurns()
    {
        var initialTool = new SimpleTool("tool_v1", "Version 1");
        var layer = new DynamicToolboxLayer();
        layer.RegisterTool(initialTool);

        var turn1Tools = await layer.GetToolsAsync();
        Assert.Single(turn1Tools);
        Assert.Equal("tool_v1", turn1Tools[0].Definition.Name);

        var dynamicTool = new SimpleTool("tool_v2", "Version 2 dynamically added");
        layer.RegisterTool(dynamicTool);

        var turn2Tools = await layer.GetToolsAsync();
        Assert.Equal(2, turn2Tools.Count);
        Assert.Contains(turn2Tools, t => t.Definition.Name == "tool_v1");
        Assert.Contains(turn2Tools, t => t.Definition.Name == "tool_v2");
    }
}
