using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using AgentCore.Tool.Tools;

namespace AgentCore.Tests;

public class AgentRuntimeTests
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


    [Fact]
    public async Task AddTool_RegistersToolsCorrectly()
    {
        var tooling = new Toolbox().AddTool<StaticTestTools>().AddTool(new InstanceTestTools());
        Assert.Equal(4, (await tooling.GetToolsAsync()).Count);
    }

    [Fact]
    public void Constructor_WithoutProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new Agent(null!));
    }

    private class MemoryLoggerDecorator(IContext? inner = null) : ContextLayer(inner)
    {
        public List<string> CallLog { get; } = [];
        public override Task<IReadOnlyList<Message>> ReadAsync(CancellationToken ct = default) { CallLog.Add("ReadAsync"); return base.ReadAsync(ct); }
        public override async IAsyncEnumerable<IMessageEvent> WriteAsync(IAsyncEnumerable<IMessageEvent> events, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            CallLog.Add("WriteAsync");
            await foreach (var evt in base.WriteAsync(events, ct).ConfigureAwait(false)) yield return evt;
        }
    }

    [Fact]
    public async Task Agent_InjectsAndSequencesDecoratorsCorrectly()
    {
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new Text("Acknowledged"));

        var baseMemory = new Context.ChatContext(contextWindow: 50000);
        var decoratorInstance = new MemoryLoggerDecorator(baseMemory);

        var agent = new Agent(mockProvider, context: decoratorInstance);

        Assert.NotNull(agent);
        await agent.WithResponse<string>(new Text("Hello"));
        Assert.Contains("WriteAsync", decoratorInstance.CallLog);
        Assert.Contains("ReadAsync", decoratorInstance.CallLog);
    }

    private class TestLlmDecorator(string name, List<string> callOrder, ILLM? inner = null) : LLMLayer(inner)
    {
        public override IAsyncEnumerable<IMessageEvent> GenerateAsync(IReadOnlyList<Message> messages, IReadOnlyList<ToolDefinition>? tools = null, JsonSchema? responseSchema = null, CancellationToken ct = default)
        {
            callOrder.Add(name);
            return base.GenerateAsync(messages, tools, responseSchema, ct);
        }
    }

    private class TestMemoryDecorator(string name, List<string> callOrder, IContext? inner = null) : ContextLayer(inner)
    {
        public override Task<IReadOnlyList<Message>> ReadAsync(CancellationToken ct = default) { callOrder.Add(name); return base.ReadAsync(ct); }
        public override async IAsyncEnumerable<IMessageEvent> WriteAsync(IAsyncEnumerable<IMessageEvent> events, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            callOrder.Add(name);
            await foreach (var evt in base.WriteAsync(events, ct).ConfigureAwait(false)) yield return evt;
        }
    }

    [Fact]
    public async Task Agent_AppliesLlmAndMemoryLayersInPipelineOrder()
    {
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new Text("Hi"));
        var callOrder = new List<string>();

        var llm = new TestLlmDecorator("LlmLayer2", callOrder, new TestLlmDecorator("LlmLayer1", callOrder, mockProvider));
        var context = new TestMemoryDecorator("MemoryLayer2", callOrder, new TestMemoryDecorator("MemoryLayer1", callOrder, new Context.ChatContext()));

        var agent = new Agent(llm, context: context);

        Assert.NotNull(agent);
        await agent.WithResponse<string>(new Text("Hello"));

        Assert.Equal(new[] { "MemoryLayer2", "MemoryLayer1", "MemoryLayer2", "MemoryLayer1", "LlmLayer2", "LlmLayer1" }, callOrder);
    }

    [Fact]
    public void Agent_ExposesRequiredServices()
    {
        var mockProvider = new MockLLMProvider();
        var agent = new Agent(mockProvider);

        Assert.NotNull(agent.LLM);
        Assert.NotNull(agent.Context);
        Assert.NotNull(agent.Toolbox);
    }

    [Fact]
    public async Task Agent_LambdaConstructor_ConfiguresToolboxAndContext()
    {
        var mockProvider = new MockLLMProvider();
        var agent = new Agent(
            mockProvider,
            toolbox: tools => tools.AddTool<StaticTestTools>(),
            context: ctx => ctx);

        Assert.NotNull(agent.Toolbox);
        Assert.Equal(2, (await agent.Toolbox.GetToolsAsync()).Count);
    }

    [Fact]
    public async Task Agent_LivingFacades_CanAddAndRemoveLayersInPlace()
    {
        var mockProvider = new MockLLMProvider();
        var agent = new Agent(mockProvider);

        agent.Toolbox.AddTool<StaticTestTools>();
        Assert.Equal(2, (await agent.Toolbox.GetToolsAsync()).Count);

        var layer = new ToolboxLayer();
        agent.Toolbox.AddLayer(layer);
        agent.Toolbox.RemoveLayer<ToolboxLayer>();
        Assert.Equal(2, (await agent.Toolbox.GetToolsAsync()).Count);
    }
}
