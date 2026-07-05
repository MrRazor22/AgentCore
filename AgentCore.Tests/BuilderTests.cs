using System;
using Xunit;
using AgentCore;
using AgentCore.LLM;
using AgentCore.Memory;
using AgentCore.Tokens;
using AgentCore.Tooling;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore.Tests;

public class BuilderTests
{
    [Fact]
    public void Build_WithoutProvider_ThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new AgentBuilder()
            .WithName("no-provider-agent");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("No LLM provider registered", ex.Message);
    }

    [Fact]
    public void Build_WithDuplicateToolNames_ThrowsInvalidOperationException()
    {
        // Arrange
        var provider = new MockLLMProvider();
        var builder = new AgentBuilder()
            .WithName("dup-tool-agent")
            .WithProvider(provider)
            .WithTools(r =>
            {
                r.Register(() => "first", "duplicate_name");
                r.Register(() => "second", "duplicate_name");
            });

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }



    [Fact]
    public void Build_Defaults_SetsUpFallbackInstances()
    {
        // Arrange
        var provider = new MockLLMProvider();
        var builder = new AgentBuilder()
            .WithName("default-setup-agent")
            .WithProvider(provider);

        // Act
        var agent = builder.Build();

        // Assert - If it succeeded without throwing, it means it correctly defaulted memory, counter, etc.
        Assert.NotNull(agent);
    }

    [Fact]
    public void Build_Idempotency_ProducesValidIndependentAgents()
    {
        // Arrange
        var provider = new MockLLMProvider();
        var builder = new AgentBuilder()
            .WithName("idempotency-agent")
            .WithProvider(provider);

        // Act
        var agent1 = builder.Build();
        var agent2 = builder.Build();

        // Assert
        Assert.NotNull(agent1);
        Assert.NotNull(agent2);
        Assert.NotSame(agent1, agent2);
    }
}
