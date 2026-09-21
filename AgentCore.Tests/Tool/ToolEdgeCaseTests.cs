using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;
using AgentCore.Tool;
using Xunit;

namespace AgentCore.Tests.Tool;

public class ToolEdgeCaseTests
{
    public class ComplexParamsTool
    {
        [Tool("ComplexAction", "Executes with complex nested parameters and nullables.")]
        public string Execute(
            [Description("Required string")] string requiredStr,
            [Description("Optional int")] int? optionalInt = null,
            [Description("Tags array")] string[]? tags = null)
        {
            return $"{requiredStr}_{optionalInt ?? 0}_{(tags != null ? string.Join(",", tags) : "none")}";
        }

        [Tool("ThrowingAction", "Always throws to test tool error containment.")]
        public Task<string> ThrowError()
        {
            throw new InvalidOperationException("Simulated tool crash");
        }
    }

    [Fact]
    public async Task ToolboxLayer_WithNullableAndArrayParameters_ParsesAndExecutes()
    {
        var target = new ComplexParamsTool();
        var defs = MethodTool.FromInstance(target);
        var toolbox = new ToolboxLayer(defs);

        var args = new JsonObject
        {
            ["requiredStr"] = "hello",
            ["optionalInt"] = 77,
            ["tags"] = new JsonArray("alpha", "beta")
        };

        var call = new ToolCall("call_1", "ComplexAction", args.ToJsonString());
        var results = new List<IAgentEvent>();

        await foreach (var evt in toolbox.ExecuteAsync([call]))
        {
            results.Add(evt);
        }

        var delta = Assert.Single(results);
        var msgDelta = Assert.IsType<MessageDelta>(delta);
        var toolResult = Assert.IsType<ToolResult>(msgDelta.Content);
        Assert.False(toolResult.IsError);
        Assert.Contains("hello_77_alpha,beta", toolResult.Contents[0].ToString());
    }

    [Fact]
    public async Task ToolboxLayer_ToolThrowsException_EmitsErrorToolResultWithoutTerminating()
    {
        var target = new ComplexParamsTool();
        var defs = MethodTool.FromInstance(target);
        var toolbox = new ToolboxLayer(defs);

        var call = new ToolCall("call_err", "ThrowingAction", "{}");
        var results = new List<IAgentEvent>();

        await foreach (var evt in toolbox.ExecuteAsync([call]))
        {
            results.Add(evt);
        }

        var delta = Assert.Single(results);
        var msgDelta = Assert.IsType<MessageDelta>(delta);
        var toolResult = Assert.IsType<ToolResult>(msgDelta.Content);
        Assert.True(toolResult.IsError);
        Assert.Contains("Simulated tool crash", toolResult.Contents[0].ToString());
    }

    [Fact]
    public async Task ToolboxLayer_MalformedJsonArguments_GracefullyReportsError()
    {
        var target = new ComplexParamsTool();
        var defs = MethodTool.FromInstance(target);
        var toolbox = new ToolboxLayer(defs);

        var call = new ToolCall("call_bad", "ComplexAction", "{ invalid json !!");
        var results = new List<IAgentEvent>();

        await foreach (var evt in toolbox.ExecuteAsync([call]))
        {
            results.Add(evt);
        }

        var delta = Assert.Single(results);
        var msgDelta = Assert.IsType<MessageDelta>(delta);
        var toolResult = Assert.IsType<ToolResult>(msgDelta.Content);
        Assert.True(toolResult.IsError);
    }
}
