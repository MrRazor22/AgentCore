using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AgentCore;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Tokens;
using AgentCore.MCP.Server;
using AgentCore.MCP.Client;
using AgentCore.Tooling;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AgentCore.Tests;

public class McpTests
{
    [Fact]
    public void AgentMcpServer_BuildAgentTool_GeneratesCorrectSchema()
    {
        // Arrange
        var agent = new LLMAgent(
            new LLMExecutor(new MockLLMProvider(), new MockToolRegistry(), new ApproximateTokenCounter(), new MockTokenManager(), Microsoft.Extensions.Logging.Abstractions.NullLogger<LLMExecutor>.Instance),
            new MockToolExecutor(),
            new MockMemory(),
            new ApproximateTokenCounter(),
            new LLMOptions(),
            new AgentConfig(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LLMAgent>.Instance
        );
        var options = new AgentMcpServerOptions
        {
            Name = "test-agent-mcp",
            Description = "Custom description for MCP tool"
        };
        var server = new AgentMcpServer(agent, options);

        // Act - Call private BuildAgentTool using reflection
        var buildMethod = typeof(AgentMcpServer).GetMethod("BuildAgentTool", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(buildMethod);

        var tool = buildMethod.Invoke(server, null) as ModelContextProtocol.Protocol.Tool;

        // Assert
        Assert.NotNull(tool);
        Assert.Equal("test-agent-mcp", tool.Name);
        Assert.Equal("Custom description for MCP tool", tool.Description);
        
        var inputSchemaText = tool.InputSchema.GetRawText();
        Assert.Contains("input", inputSchemaText);
    }

    [Fact]
    public async Task AgentMcpServer_HandleCallToolAsync_InvokesAgentCorrectly()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueAction(() => new IContentDelta[]
        {
            new TextDelta("Hello from MCP agent!"),
            new MetaDelta(null, new TokenUsage(10, 10, 0))
        });

        var executor = new LLMExecutor(
            mockProvider,
            new MockToolRegistry(),
            new ApproximateTokenCounter(),
            new MockTokenManager(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LLMExecutor>.Instance
        );

        var agent = new LLMAgent(
            executor,
            new MockToolExecutor(),
            new MockMemory(),
            new ApproximateTokenCounter(),
            new LLMOptions(),
            new AgentConfig(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LLMAgent>.Instance
        );

        var options = new AgentMcpServerOptions
        {
            Name = "test-agent-mcp"
        };
        var server = new AgentMcpServer(agent, options);

        // Act - Call private HandleCallToolAsync using reflection
        var handleMethod = typeof(AgentMcpServer).GetMethod("HandleCallToolAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(handleMethod);

        var arguments = new Dictionary<string, JsonElement>
        {
            ["input"] = JsonSerializer.SerializeToElement("User query"),
            ["session_id"] = JsonSerializer.SerializeToElement("session-mcp")
        };

        var requestParams = new CallToolRequestParams
        {
            Name = "test-agent-mcp",
            Arguments = arguments
        };

        // Construct RequestContext<CallToolRequestParams>
        // Depending on assembly visibility, we can construct using reflection or public constructors
        var requestContextType = typeof(RequestContext<>).MakeGenericType(typeof(CallToolRequestParams));
        var constructor = requestContextType.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(string), typeof(CallToolRequestParams), typeof(Func<ValueTask>), typeof(Func<Exception, ValueTask>) },
            null
        );

        if (constructor != null)
        {
            var requestContext = constructor.Invoke(new object[]
            {
                "123", // requestId
                requestParams,
                new Func<ValueTask>(() => ValueTask.CompletedTask),
                new Func<Exception, ValueTask>(_ => ValueTask.CompletedTask)
            });

            var resultTask = handleMethod.Invoke(server, new[] { requestContext, CancellationToken.None });
            Assert.NotNull(resultTask);

            var result = await (ValueTask<CallToolResult>)resultTask;
            Assert.NotNull(result);
            Assert.Single(result.Content);
            var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
            Assert.Contains("Hello from MCP agent!", textBlock.Text);
        }
    }
}
