using AgentCore.LLM.Chat;
using AgentCore.Tools;
using AgentCore.LLM;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using System;

namespace AgentCore.Tests;

public class AgentBuilderTests
{
    private class StaticTestTools
    {
        [Tool]
        public static string StaticTool1() => "static1";

        [Tool]
        public static string StaticTool2() => "static2";
    }

    private class InstanceTestTools
    {
        [Tool]
        public string InstanceTool1() => "instance1";

        [Tool]
        public string InstanceTool2() => "instance2";
    }

    private class MixedTestTools
    {
        [Tool]
        public static string StaticTool() => "static";

        [Tool]
        public string InstanceTool() => "instance";
    }

    [Fact]
    public void WithTools_Generic_RegistersStaticTools()
    {
        var builder = Agent.Create().WithLLM(new MockLLMProvider());
        builder.WithTools<StaticTestTools>();

        var agent = builder.Build();
        Assert.NotNull(agent);
    }

    [Fact]
    public void WithTools_Instance_RegistersInstanceTools()
    {
        var builder = Agent.Create().WithLLM(new MockLLMProvider());
        var instance = new InstanceTestTools();
        builder.WithTools(instance);

        var agent = builder.Build();
        Assert.NotNull(agent);
    }

    [Fact]
    public void WithTools_Generic_ThrowsForInstanceMethods()
    {
        var builder = Agent.Create().WithLLM(new MockLLMProvider());
        var ex = Assert.Throws<ArgumentException>(() => { builder.WithTools<InstanceTestTools>(); });
        Assert.Contains("instance method", ex.Message);
    }
    
    [Fact]
    public void WithTools_Instance_RegistersMixedTools()
    {
        var builder = Agent.Create().WithLLM(new MockLLMProvider());
        var instance = new MixedTestTools();
        builder.WithTools(instance);

        var agent = builder.Build();
        Assert.NotNull(agent);
    }

    [Fact]
    public void Build_WithoutProvider_ThrowsInvalidOperationException()
    {
        var builder = Agent.Create();
        Assert.Throws<InvalidOperationException>(() => { builder.Build(); });
    }

    private class MemoryLoggerDecorator(Memory.IMemory inner) : Memory.IMemory
    {
        public List<string> CallLog { get; } = new();

        public Task<List<Message>> PrepareAsync(
            Message userInput,
            CancellationToken ct = default)
        {
            CallLog.Add("Recall");
            return inner.PrepareAsync(userInput, ct);
        }

        public Task RememberAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
        {
            CallLog.Add("Remember");
            return inner.RememberAsync(completedTurn, ct);
        }

        public Task ClearAsync(CancellationToken ct = default)
        {
            CallLog.Add("Clear");
            return inner.ClearAsync(ct);
        }

        public Task RestoreAsync(IReadOnlyList<Message> history, CancellationToken ct = default)
        {
            CallLog.Add("Restore");
            return inner.RestoreAsync(history, ct);
        }
    }

    [Fact]
    public async Task Build_InjectsAndSequencesDecoratorsCorrectly()
    {
        MemoryLoggerDecorator? decoratorInstance = null;
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new Text("Acknowledged"));

        var baseMemory = new Memory.WorkingMemory(
            new ApproximateTokenCounter(),
            new LLMCapabilities(),
            Array.Empty<Tool>(),
            null);
            
        decoratorInstance = new MemoryLoggerDecorator(baseMemory);

        var agent = Agent.Create()
            .WithLLM(mockProvider)
            .WithMemory(decoratorInstance)
            .Build();

        Assert.NotNull(agent);
        await agent.InvokeAsync<string>(new Text("Hello"));
        Assert.NotNull(decoratorInstance);
        Assert.Contains("Recall", decoratorInstance.CallLog);
    }

    [Fact]
    public void Build_AppliesLlmAndMemoryLayersInPipelineOrder()
    {
        var mockProvider = new MockLLMProvider();
        var callOrder = new List<string>();

        var agent = Agent.Create()
            .WithLLM(mockProvider)
            .AddLlmLayer(inner =>
            {
                callOrder.Add("LlmLayer1");
                return inner;
            })
            .AddLlmLayer(inner =>
            {
                callOrder.Add("LlmLayer2");
                return inner;
            })
            .AddMemoryLayer(inner =>
            {
                callOrder.Add("MemoryLayer1");
                return inner;
            })
            .AddMemoryLayer(inner =>
            {
                callOrder.Add("MemoryLayer2");
                return inner;
            })
            .Build();

        Assert.NotNull(agent);
        Assert.Equal(new[] { "LlmLayer1", "LlmLayer2", "MemoryLayer1", "MemoryLayer2" }, callOrder);
    }
}
