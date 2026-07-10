using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AgentCore;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore.Tests;

public class AgentTests
{
    private class TestDto
    {
        public string? Message { get; set; }
        public int Number { get; set; }
    }

    [Fact]
    public async Task InvokeAsync_NormalFlow_ReturnsResponse()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueAction(() => new IContentDelta[]
        {
            new TextDelta("Model response content"),
            new MetaDelta(null, new TokenUsage(15, 10, 0))
        });

        var executor = new LLMExecutor(
            mockProvider,
            new MockToolRegistry(),
            new ApproximateTokenCounter(),
            new MockTokenManager(),
            NullLogger<LLMExecutor>.Instance
        );

        var agent = new LLMAgent(
            executor,
            new MockToolExecutor(),
            new MockMemory(),
            new ApproximateTokenCounter(),
            new LLMOptions(),
            new AgentConfig { Name = "test" },
            NullLogger<LLMAgent>.Instance
        );

        // Act
        var response = await agent.InvokeAsync(new Text("Hello"));

        // Assert
        Assert.Equal("Model response content", response.ForLlm());
    }

    [Fact]
    public async Task InvokeAsync_StructuredOutput_ParsesValidJson()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueAction(() => new IContentDelta[]
        {
            new TextDelta("{\"Message\":\"All good\",\"Number\":42}"),
            new MetaDelta(null, new TokenUsage(10, 10, 0))
        });

        var executor = new LLMExecutor(
            mockProvider,
            new MockToolRegistry(),
            new ApproximateTokenCounter(),
            new MockTokenManager(),
            NullLogger<LLMExecutor>.Instance
        );

        var agent = new LLMAgent(
            executor,
            new MockToolExecutor(),
            new MockMemory(),
            new ApproximateTokenCounter(),
            new LLMOptions(),
            new AgentConfig(),
            NullLogger<LLMAgent>.Instance
        );

        // Act
        var result = await agent.InvokeAsync<TestDto>(new Text("Get schema data"));

        // Assert
        Assert.NotNull(result);
        Assert.Equal("All good", result.Message);
        Assert.Equal(42, result.Number);
    }

    [Fact]
    public async Task InvokeAsync_StructuredOutput_MalformedJson_ThrowsJsonException()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueAction(() => new IContentDelta[]
        {
            new TextDelta("{\"Message\":\"All good\", malformed"),
            new MetaDelta(null, new TokenUsage(10, 10, 0))
        });

        var executor = new LLMExecutor(
            mockProvider,
            new MockToolRegistry(),
            new ApproximateTokenCounter(),
            new MockTokenManager(),
            NullLogger<LLMExecutor>.Instance
        );

        var agent = new LLMAgent(
            executor,
            new MockToolExecutor(),
            new MockMemory(),
            new ApproximateTokenCounter(),
            new LLMOptions(),
            new AgentConfig(),
            NullLogger<LLMAgent>.Instance
        );

        // Act & Assert
        await Assert.ThrowsAsync<JsonException>(async () =>
        {
            await agent.InvokeAsync<TestDto>(new Text("Get schema data"));
        });
    }

    [Fact]
    public async Task InvokeAsync_StructuredOutput_MissingProperty_DeserializesDefaults()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueAction(() => new IContentDelta[]
        {
            // Missing "Message" property
            new TextDelta("{\"Number\":100}"),
            new MetaDelta(null, new TokenUsage(10, 10, 0))
        });

        var executor = new LLMExecutor(
            mockProvider,
            new MockToolRegistry(),
            new ApproximateTokenCounter(),
            new MockTokenManager(),
            NullLogger<LLMExecutor>.Instance
        );

        var agent = new LLMAgent(
            executor,
            new MockToolExecutor(),
            new MockMemory(),
            new ApproximateTokenCounter(),
            new LLMOptions(),
            new AgentConfig(),
            NullLogger<LLMAgent>.Instance
        );

        // Act
        var result = await agent.InvokeAsync<TestDto>(new Text("Get schema data"));

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.Message);
        Assert.Equal(100, result.Number);
    }

    [Fact]
    public async Task InvokeAsync_StructuredOutput_ExtraProperty_Ignored()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueAction(() => new IContentDelta[]
        {
            // Extra "UnknownProperty"
            new TextDelta("{\"Message\":\"Ignored\",\"Number\":50,\"UnknownProperty\":\"extra\"}"),
            new MetaDelta(null, new TokenUsage(10, 10, 0))
        });

        var executor = new LLMExecutor(
            mockProvider,
            new MockToolRegistry(),
            new ApproximateTokenCounter(),
            new MockTokenManager(),
            NullLogger<LLMExecutor>.Instance
        );

        var agent = new LLMAgent(
            executor,
            new MockToolExecutor(),
            new MockMemory(),
            new ApproximateTokenCounter(),
            new LLMOptions(),
            new AgentConfig(),
            NullLogger<LLMAgent>.Instance
        );

        // Act
        var result = await agent.InvokeAsync<TestDto>(new Text("Get schema data"));

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Ignored", result.Message);
        Assert.Equal(50, result.Number);
    }

    [Fact]
    public async Task InvokeAsync_StructuredOutput_WrongType_ThrowsJsonExceptionOrInvalidOperation()
    {
        // Arrange
        var mockProvider = new MockLLMProvider();
        mockProvider.EnqueueAction(() => new IContentDelta[]
        {
            // Number parameter has string value
            new TextDelta("{\"Message\":\"Wrong\",\"Number\":\"not_a_number\"}"),
            new MetaDelta(null, new TokenUsage(10, 10, 0))
        });

        var executor = new LLMExecutor(
            mockProvider,
            new MockToolRegistry(),
            new ApproximateTokenCounter(),
            new MockTokenManager(),
            NullLogger<LLMExecutor>.Instance
        );

        var agent = new LLMAgent(
            executor,
            new MockToolExecutor(),
            new MockMemory(),
            new ApproximateTokenCounter(),
            new LLMOptions(),
            new AgentConfig(),
            NullLogger<LLMAgent>.Instance
        );

        // Act & Assert
        await Assert.ThrowsAsync<JsonException>(async () =>
        {
            await agent.InvokeAsync<TestDto>(new Text("Get schema data"));
        });
    }
}
