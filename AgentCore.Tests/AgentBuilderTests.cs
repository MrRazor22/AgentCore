using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;

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

        public MemoryLoggerDecorator(IContext inner) : base(inner)
        {
        }

        public override Task<IReadOnlyList<Message>> BuildPromptAsync(
            IReadOnlyList<Message> uncommittedMessages,
            CancellationToken ct = default)
        {
            CallLog.Add("Prepare");
            return base.BuildPromptAsync(uncommittedMessages, ct);
        }

        public override async Task CommitAsync(
            TokenUsage usage,
            IReadOnlyList<Message> response,
            CancellationToken ct = default)
        {
            CallLog.Add("Commit");
            await base.CommitAsync(usage, response, ct).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task Build_InjectsAndSequencesDecoratorsCorrectly()
    {
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new Text("Acknowledged"));

        var baseMemory = new ChatContext(
            contextWindow: 50000
        );

        MemoryLoggerDecorator? decoratorInstance = null;

        var builder = Agent.Create()
            .WithLLM(mockProvider)
            .WithContext(baseMemory)
            .AddContextLayer(inner =>
            {
                decoratorInstance = new MemoryLoggerDecorator(inner);
                return decoratorInstance;
            });

        var agent = builder.Build();

        Assert.NotNull(agent);
        await agent.InvokeAsync<string>(new Text("Hello"));
        Assert.NotNull(decoratorInstance);
        Assert.Contains("Commit", decoratorInstance.CallLog);
    }

    private class TestLlmDecorator : LLMLayer
    {
        private readonly string _name;
        private readonly List<string> _callOrder;

        public TestLlmDecorator(string name, List<string> callOrder, ILLM inner) : base(inner)
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

        public TestMemoryDecorator(string name, List<string> callOrder, IContext inner) : base(inner)
        {
            _name = name;
            _callOrder = callOrder;
        }

        public override Task<IReadOnlyList<Message>> BuildPromptAsync(
            IReadOnlyList<Message> uncommittedMessages,
            CancellationToken ct = default)
        {
            _callOrder.Add(_name);
            return base.BuildPromptAsync(uncommittedMessages, ct);
        }

        public override async Task CommitAsync(
            TokenUsage usage,
            IReadOnlyList<Message> response,
            CancellationToken ct = default)
        {
            _callOrder.Add(_name);
            await base.CommitAsync(usage, response, ct).ConfigureAwait(false);
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
            .AddLLMLayer(inner => new TestLlmDecorator("LlmLayer1", callOrder, inner))
            .AddLLMLayer(inner => new TestLlmDecorator("LlmLayer2", callOrder, inner))
            .AddContextLayer(inner => new TestMemoryDecorator("MemoryLayer1", callOrder, inner))
            .AddContextLayer(inner => new TestMemoryDecorator("MemoryLayer2", callOrder, inner));

        var agent = builder.Build();

        Assert.NotNull(agent);
        await agent.InvokeAsync<string>(new Text("Hello"));

        Assert.Equal(new[] { "MemoryLayer2", "MemoryLayer1", "LlmLayer2", "LlmLayer1", "MemoryLayer2", "MemoryLayer1" }, callOrder);
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
}
