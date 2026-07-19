using AgentCore.LLM.Chat;
using AgentCore.Tools;
using AgentCore.LLM;

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
        var builder = Agent.Create().WithProvider(new MockLLMProvider());
        builder.WithTools<StaticTestTools>();

        var agent = builder.Build();
        Assert.NotNull(agent);
    }

    [Fact]
    public void WithTools_Instance_RegistersInstanceTools()
    {
        var builder = Agent.Create().WithProvider(new MockLLMProvider());
        var instance = new InstanceTestTools();
        builder.WithTools(instance);

        var agent = builder.Build();
        Assert.NotNull(agent);
    }

    [Fact]
    public void WithTools_Generic_ThrowsForInstanceMethods()
    {
        var builder = Agent.Create().WithProvider(new MockLLMProvider());
        var ex = Assert.Throws<ArgumentException>(() => { builder.WithTools<InstanceTestTools>(); });
        Assert.Contains("instance method", ex.Message);
    }
    
    [Fact]
    public void WithTools_Instance_RegistersMixedTools()
    {
        var builder = Agent.Create().WithProvider(new MockLLMProvider());
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

    private class MemoryLoggerDecorator(Memory.IContextService inner) : Memory.IContextService
    {
        public List<string> CallLog { get; } = new();

        public Task<List<Message>> PrepareAsync(
            Message userInput,
            CancellationToken ct = default)
        {
            CallLog.Add("Recall");
            return inner.PrepareAsync(userInput, ct);
        }

        public Task UpdateAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
        {
            CallLog.Add("Remember");
            return inner.UpdateAsync(completedTurn, ct);
        }
    }

    [Fact]
    public async Task Build_InjectsAndSequencesDecoratorsCorrectly()
    {
        MemoryLoggerDecorator? decoratorInstance = null;
        var mockProvider = new MockLLMProvider();
        mockProvider.Enqueue(new TextDelta("Acknowledged"));

        var agent = Agent.Create()
            .WithProvider(mockProvider)
            .AddContextLayer(inner =>
            {
                decoratorInstance = new MemoryLoggerDecorator(inner);
                return decoratorInstance;
            })
            .Build();

        Assert.NotNull(agent);
        await agent.InvokeAsync<string>(new Text("Hello"));
        Assert.NotNull(decoratorInstance);
        Assert.Contains("Recall", decoratorInstance.CallLog);
    }
}

