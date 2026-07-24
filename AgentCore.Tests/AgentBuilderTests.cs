using AgentCore.LLM.Chat;
using AgentCore.Tools;
using AgentCore.LLM;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using System;
using AgentCore.Context;

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

    private class MemoryLoggerDecorator : ContextLayer
    {
        public List<string> CallLog { get; } = new();

        public override IReadOnlyList<Message> Messages
        {
            get
            {
                CallLog.Add("Get");
                return base.Messages;
            }
        }

        public override Task AddAsync(Message message, CancellationToken ct = default)
        {
            CallLog.Add("Add");
            return base.AddAsync(message, ct);
        }

        public override Task ClearAsync(CancellationToken ct = default)
        {
            CallLog.Add("Clear");
            return base.ClearAsync(ct);
        }

        public override Task AddRangeAsync(IEnumerable<Message> messages, CancellationToken ct = default)
        {
            CallLog.Add("AddRange");
            return base.AddRangeAsync(messages, ct);
        }
    }

    [Fact]
    public async Task Build_InjectsAndSequencesDecoratorsCorrectly()
    {
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new Text("Acknowledged"));

        var baseMemory = new ChatContext(
            new ApproximateTokenCounter(),
            new LLMCapabilities(),
            Array.Empty<Tool>(),
            null);
            
        var decoratorInstance = new MemoryLoggerDecorator();

        var builder = Agent.Create()
            .WithLLM(mockProvider)
            .WithContext(baseMemory)
            .AddContextLayer(decoratorInstance);

        var agent = builder.Build();

        Assert.NotNull(agent);
        await agent.InvokeAsync<string>(new Text("Hello"));
        Assert.Contains("Add", decoratorInstance.CallLog);
    }

    private class TestLlmDecorator : LLMLayer
    {
        private readonly string _name;
        private readonly List<string> _callOrder;

        public TestLlmDecorator(string name, List<string> callOrder)
        {
            _name = name;
            _callOrder = callOrder;
        }

        public override IAsyncEnumerable<ILLMOutput> StreamAsync(IReadOnlyList<Message> messages, LLMOptions? options = null, IReadOnlyList<Tool>? tools = null, CancellationToken ct = default)
        {
            _callOrder.Add(_name);
            return base.StreamAsync(messages, options, tools, ct);
        }
    }

    private class TestMemoryDecorator : ContextLayer
    {
        private readonly string _name;
        private readonly List<string> _callOrder;

        public TestMemoryDecorator(string name, List<string> callOrder)
        {
            _name = name;
            _callOrder = callOrder;
        }

        public override Task AddAsync(Message newInput, CancellationToken ct = default)
        {
            _callOrder.Add(_name);
            return base.AddAsync(newInput, ct);
        }
    }

    [Fact]
    public async Task Build_AppliesLlmAndMemoryLayersInPipelineOrder()
    {
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new Text("Hi"));
        var callOrder = new List<string>();

        var builder = Agent.Create()
            .WithLLM(mockProvider)
            .AddLLMLayer(new TestLlmDecorator("LlmLayer1", callOrder))
            .AddLLMLayer(new TestLlmDecorator("LlmLayer2", callOrder))
            .AddContextLayer(new TestMemoryDecorator("MemoryLayer1", callOrder))
            .AddContextLayer(new TestMemoryDecorator("MemoryLayer2", callOrder));

        var agent = builder.Build();

        Assert.NotNull(agent);
        await agent.InvokeAsync<string>(new Text("Hello"));

        Assert.Equal(new[] { "MemoryLayer2", "MemoryLayer1", "LlmLayer2", "LlmLayer1", "MemoryLayer2", "MemoryLayer1" }, callOrder);
    }

    [Fact]
    public void Build_ThrowsOnDecoratorReuse()
    {
        var mockProvider = new MockLLMProvider();
        var decorator = new TestMemoryDecorator("Shared", new List<string>());

        var builder1 = Agent.Create()
            .WithLLM(mockProvider)
            .AddContextLayer(decorator);

        builder1.Build();

        var builder2 = Agent.Create()
            .WithLLM(mockProvider)
            .AddContextLayer(decorator);

        Assert.Throws<InvalidOperationException>(() => builder2.Build());
    }

    [Fact]
    public void Builder_ExposesRequiredServices()
    {
        var mockProvider = new MockLLMProvider();
        var builder = Agent.Create()
            .WithLLM(mockProvider);

        var agent = builder.Build();

        var llm = builder.GetRequiredService<ILLM>();
        var memory = builder.GetRequiredService<IContext>();
        var tooling = builder.GetRequiredService<ITooling>();

        Assert.NotNull(llm);
        Assert.NotNull(memory);
        Assert.NotNull(tooling);
    }

    [Fact]
    public void Build_AutomaticallyWiresTokenCalibrationLayerAsOutermost()
    {
        var mockProvider = new MockLLMProvider();
        var builder = Agent.Create()
            .WithLLM(mockProvider);

        var agent = builder.Build();

        var llm = builder.GetRequiredService<ILLM>();
        Assert.IsType<TokenCalibrationLayer>(llm);
        
        var calibrationLayer = (TokenCalibrationLayer)llm;
        Assert.Same(mockProvider, calibrationLayer.Inner);
    }
}
