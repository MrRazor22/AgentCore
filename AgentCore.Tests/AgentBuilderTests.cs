using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
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

        public override Task<IReadOnlyList<Message>> GetMessagesAsync(
            CancellationToken ct = default)
        {
            CallLog.Add("GetMessages");
            return base.GetMessagesAsync(ct);
        }

        public override async Task AddAsync(
            IReadOnlyList<Message> messages,
            CancellationToken ct = default)
        {
            CallLog.Add("Add");
            await base.AddAsync(messages, ct).ConfigureAwait(false);
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
        await agent.InvokeAsync<string>(new Text("Hello"));
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

        public override IAsyncEnumerable<IMessageEvent> StreamAsync(IReadOnlyList<Message> messages, JsonSchema? responseSchema = null, IReadOnlyList<ToolDefinition>? tools = null, CancellationToken ct = default)
        {
            _callOrder.Add(_name);
            return base.StreamAsync(messages, responseSchema, tools, ct);
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

        public override Task<IReadOnlyList<Message>> GetMessagesAsync(
            CancellationToken ct = default)
        {
            _callOrder.Add(_name);
            return base.GetMessagesAsync(ct);
        }

        public override async Task AddAsync(
            IReadOnlyList<Message> messages,
            CancellationToken ct = default)
        {
            _callOrder.Add(_name);
            await base.AddAsync(messages, ct).ConfigureAwait(false);
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
        await agent.InvokeAsync<string>(new Text("Hello"));

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

        var llm = builder.GetRequiredService<ILLM>();
        var memory = builder.GetRequiredService<IContext>();
        var tooling = builder.GetRequiredService<ITooling>();

        Assert.NotNull(llm);
        Assert.NotNull(memory);
        Assert.NotNull(tooling);
    }

    private record CustomTestText(string Value) : Text(Value)
    {
        public override string ToString() => Value;
        public override IContent Truncate(int maxTokens, string? notice = null)
        {
            return new CustomTestText($"[CUSTOM_PREFIX]{base.Truncate(maxTokens, notice)}");
        }
    }

    [Fact]
    public async Task Builder_UseContent_IsScopedPerAgentAndDoesNotLeak()
    {
        var mockProvider = new MockLLMProvider();

        // Agent 1 with CustomTestText
        var agent1 = Agent.Create()
            .WithInstructions("Instructions for Agent 1")
            .WithLLM(lf => mockProvider)
            .WithTools<StaticTestTools>()
            .UseContent<Text, CustomTestText>(t => new CustomTestText(t.Value));

        var built1 = agent1.Build();
        var context1 = agent1.GetRequiredService<IContext>();
        var messages1 = await context1.GetMessagesAsync();
        Assert.IsType<CustomTestText>(messages1[0].Contents[0]);

        var tooling1 = agent1.GetRequiredService<ITooling>();
        var result1 = await tooling1.ExecuteAsync(new ToolCall("1", "StaticTool1", new System.Text.Json.Nodes.JsonObject()));
        Assert.IsType<CustomTestText>(result1.Contents[0]);

        // Agent 2 without UseContent (uses default Text)
        var agent2 = Agent.Create()
            .WithInstructions("Instructions for Agent 2")
            .WithLLM(lf => mockProvider)
            .WithTools<StaticTestTools>();

        var built2 = agent2.Build();
        var context2 = agent2.GetRequiredService<IContext>();
        var messages2 = await context2.GetMessagesAsync();
        Assert.IsType<Text>(messages2[0].Contents[0]);
        Assert.IsNotType<CustomTestText>(messages2[0].Contents[0]);

        var tooling2 = agent2.GetRequiredService<ITooling>();
        var result2 = await tooling2.ExecuteAsync(new ToolCall("2", "StaticTool1", new System.Text.Json.Nodes.JsonObject()));
        Assert.IsType<Text>(result2.Contents[0]);
        Assert.IsNotType<CustomTestText>(result2.Contents[0]);
    }
}
