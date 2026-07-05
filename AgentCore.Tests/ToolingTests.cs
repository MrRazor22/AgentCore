using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AgentCore;
using AgentCore.Conversation;
using AgentCore.Tooling;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore.Tests;

public class ToolingTests
{
    public enum ColorEnum
    {
        Red,
        Green,
        Blue
    }

        public sealed class ComplexTool
        {
            [Tool]
            public string Run(
                string requiredStr,
                [Description("An optional int parameter")] int optionalInt = 100,
                int? nullableInt = null,
                ColorEnum color = ColorEnum.Green,
                string[]? items = null)
        {
            var itemsStr = items != null ? string.Join(",", items) : "null";
            return $"requiredStr={requiredStr};optionalInt={optionalInt};nullableInt={nullableInt?.ToString() ?? "null"};color={color};items={itemsStr}";
        }
    }

    [Fact]
    public async Task ToolExecutor_HandlesReflectionMappingParameters()
    {
        // Arrange
        var registry = new ToolRegistry();
        var toolInstance = new ComplexTool();
        
        registry.RegisterAll(toolInstance);

        var toolExecutor = new ToolExecutor(registry, NullLogger<ToolExecutor>.Instance);

        // 1. Happy Path - All parameters provided
        var args1 = new JsonObject
        {
            ["requiredStr"] = "hello",
            ["optionalInt"] = 5,
            ["nullableInt"] = 10,
            ["color"] = "Blue",
            ["items"] = new JsonArray("a", "b")
        };
        var result1 = await toolExecutor.HandleToolCallAsync(new ToolCall("call1", "complex_tool.run", args1));
        Assert.Equal("requiredStr=hello;optionalInt=5;nullableInt=10;color=Blue;items=a,b", result1.Result?.ForLlm());

        // 2. Default value & Nullable parameters omitted
        var args2 = new JsonObject
        {
            ["requiredStr"] = "world"
        };
        var result2 = await toolExecutor.HandleToolCallAsync(new ToolCall("call2", "complex_tool.run", args2));
        Assert.Equal("requiredStr=world;optionalInt=100;nullableInt=null;color=Green;items=null", result2.Result?.ForLlm());

        // 3. Invalid enum conversion
        var args3 = new JsonObject
        {
            ["requiredStr"] = "test",
            ["color"] = "Yellow" // Invalid enum option
        };
        var result3 = await toolExecutor.HandleToolCallAsync(new ToolCall("call3", "complex_tool.run", args3));
        Assert.True(result3.Result is Text);
        Assert.Contains("ColorEnum", result3.Result?.ForLlm());

        // 4. Missing required parameter
        var args4 = new JsonObject
        {
            ["optionalInt"] = 42
        };
        var result4 = await toolExecutor.HandleToolCallAsync(new ToolCall("call4", "complex_tool.run", args4));
        Assert.True(result4.Result is Text);
        Assert.Contains("Missing required parameter 'requiredStr'", result4.Result?.ForLlm());
    }

    [Fact]
    public void ToolRegistry_RegistersVariousDelegatesAndGeneratesSchema()
    {
        // Arrange
        var registry = new ToolRegistry();

        // Register synchronous delegate
        registry.Register([Description("Sync Tool")] (string arg1) => $"Sync {arg1}", "sync_tool");
        
        // Register asynchronous delegate
        registry.Register([Description("Async Tool")] async (int val) => { await Task.Yield(); return $"Async {val}"; }, "async_tool");

        // Assert sync_tool schema
        var syncTool = registry.TryGet("sync_tool");
        Assert.NotNull(syncTool);
        Assert.Equal("sync_tool", syncTool.Name);
        Assert.Equal("Sync Tool", syncTool.Description);
        Assert.Equal("object", syncTool.ParametersSchema?["type"]?.ToString());

        // Assert async_tool schema
        var asyncTool = registry.TryGet("async_tool");
        Assert.NotNull(asyncTool);
        Assert.Equal("async_tool", asyncTool.Name);
        Assert.Equal("Async Tool", asyncTool.Description);
    }

    [Fact]
    public void ToolRegistry_UnregistersSuccessfully()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.Register(() => "Value", "temp_tool");

        // Act
        var resultBefore = registry.TryGet("temp_tool");
        Assert.NotNull(resultBefore);

        var unregistered = registry.Unregister("temp_tool");
        var resultAfter = registry.TryGet("temp_tool");

        // Assert
        Assert.True(unregistered);
        Assert.Null(resultAfter);
    }


}
