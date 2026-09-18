using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using AgentCore.Tool.Tools;

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
        var builder = Agent.Create().WithLLM(lf => new MockLLMProvider());
        builder.WithTools<StaticTestTools>();

        var agent = builder.Build();
        Assert.NotNull(agent);
    }

    [Fact]
    public void WithTools_Instance_RegistersInstanceTools()
    {
        var builder = Agent.Create().WithLLM(lf => new MockLLMProvider());
        var instance = new InstanceTestTools();
        builder.WithTools(instance);

        var agent = builder.Build();
        Assert.NotNull(agent);
    }

    [Fact]
    public void WithTools_Generic_ThrowsForInstanceMethods()
    {
        var builder = Agent.Create().WithLLM(lf => new MockLLMProvider());
        var ex = Assert.Throws<ArgumentException>(() => { builder.WithTools<InstanceTestTools>(); });
        Assert.Contains("instance method", ex.Message);
    }

    [Fact]
    public void WithTools_Instance_RegistersMixedTools()
    {
        var builder = Agent.Create().WithLLM(lf => new MockLLMProvider());
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

        public override Task<IReadOnlyList<Message>> PrepareAsync(
            IEnumerable<Message>? messages = null,
            CancellationToken ct = default)
        {
            CallLog.Add("GetMessages");
            return base.PrepareAsync(messages, ct);
        }

        public override async IAsyncEnumerable<IContentEvent> WriteAsync(
            IAsyncEnumerable<IMessageEvent> events,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            CallLog.Add("Add");
            await foreach (var evt in base.WriteAsync(events, ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
    }

    [Fact]
    public async Task Build_InjectsAndSequencesDecoratorsCorrectly()
    {
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new Text("Acknowledged"));

        var baseMemory = new Context.ChatContext(
            contextWindow: 50000
        );

        var decoratorInstance = new MemoryLoggerDecorator();

        var builder = Agent.Create()
            .WithLLM(lf => mockProvider)
            .WithContext(lf => baseMemory)
            .AddContextLayer(decoratorInstance);

        var agent = builder.Build();

        Assert.NotNull(agent);
        await agent.WithResponse<string>(new Text("Hello"));
        Assert.Contains("Add", decoratorInstance.CallLog);
        Assert.Contains("GetMessages", decoratorInstance.CallLog);
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

        public override IAsyncEnumerable<IMessageEvent> GenerateAsync(IReadOnlyList<Message> messages, IReadOnlyList<ToolDefinition>? tools = null, JsonSchema? responseSchema = null, CancellationToken ct = default)
        {
            _callOrder.Add(_name);
            return base.GenerateAsync(messages, tools, responseSchema, ct);
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

        public override Task<IReadOnlyList<Message>> PrepareAsync(
            IEnumerable<Message>? messages = null,
            CancellationToken ct = default)
        {
            _callOrder.Add(_name);
            return base.PrepareAsync(messages, ct);
        }

        public override async IAsyncEnumerable<IContentEvent> WriteAsync(
            IAsyncEnumerable<IMessageEvent> events,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            _callOrder.Add(_name);
            await foreach (var evt in base.WriteAsync(events, ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
    }

    [Fact]
    public async Task Build_AppliesLlmAndMemoryLayersInPipelineOrder()
    {
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new Text("Hi"));
        var callOrder = new List<string>();

        var builder = Agent.Create()
            .WithLLM(lf => mockProvider)
            .AddLLMLayer(new TestLlmDecorator("LlmLayer1", callOrder))
            .AddLLMLayer(new TestLlmDecorator("LlmLayer2", callOrder))
            .AddContextLayer(new TestMemoryDecorator("MemoryLayer1", callOrder))
            .AddContextLayer(new TestMemoryDecorator("MemoryLayer2", callOrder));

        var agent = builder.Build();

        Assert.NotNull(agent);
        await agent.WithResponse<string>(new Text("Hello"));

        Assert.Equal(new[] { "MemoryLayer2", "MemoryLayer1", "MemoryLayer2", "MemoryLayer1", "LlmLayer2", "LlmLayer1", "MemoryLayer2", "MemoryLayer1" }, callOrder);
    }

    [Fact]
    public void Build_ThrowsOnDecoratorReuse()
    {
        var mockProvider = new MockLLMProvider();
        var decorator = new TestMemoryDecorator("Shared", new List<string>());

        var builder1 = Agent.Create()
            .WithLLM(lf => mockProvider)
            .AddContextLayer(decorator);

        builder1.Build();

        var builder2 = Agent.Create()
            .WithLLM(lf => mockProvider)
            .AddContextLayer(decorator);

        Assert.Throws<InvalidOperationException>(() => builder2.Build());
    }

    [Fact]
    public void Builder_ExposesRequiredServices()
    {
        var mockProvider = new MockLLMProvider();
        var builder = Agent.Create()
            .WithLLM(lf => mockProvider);

        var agent = builder.Build();

        Assert.NotNull(agent.LLM);
        Assert.NotNull(agent.Context);
        Assert.NotNull(agent.Toolbox);
    }

    private record SampleOutput(string Name, int Value);

    [Fact]
    public async Task WithSchema_ConfiguresLLMSchema()
    {
        var mockProvider = new MockLLMProvider();
        var schema = JsonSchema.For<SampleOutput>();
        var agent = Agent.Create()
            .UseLLM(llm => llm.Use(lf => mockProvider).WithSchema(schema))
            .Build();

        await foreach (var _ in agent.InvokeStreamingAsync([new Text("Extract")])) { }

        Assert.Same(schema, mockProvider.CapturedResponseSchemas.Single());
    }
}
