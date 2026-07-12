using AgentCore.Schema;
using AgentCore.Tooling;
using Xunit;

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
        var builder = new AgentBuilder().WithProvider(new MockLLMProvider());
        builder.WithTools<StaticTestTools>();

        builder.Build();
        Assert.NotNull(builder.Services);
    }

    [Fact]
    public void WithTools_Instance_RegistersInstanceTools()
    {
        var builder = new AgentBuilder().WithProvider(new MockLLMProvider());
        var instance = new InstanceTestTools();
        builder.WithTools(instance);

        builder.Build();
        Assert.NotNull(builder.Services);
    }

    [Fact]
    public void WithTools_Generic_ThrowsForInstanceMethods()
    {
        var builder = new AgentBuilder().WithProvider(new MockLLMProvider());
        var ex = Assert.Throws<ArgumentException>(() => builder.WithTools<InstanceTestTools>());
        Assert.Contains("instance method", ex.Message);
    }
    
    [Fact]
    public void WithTools_Instance_RegistersMixedTools()
    {
        var builder = new AgentBuilder().WithProvider(new MockLLMProvider());
        var instance = new MixedTestTools();
        builder.WithTools(instance);

        builder.Build();
        Assert.NotNull(builder.Services);
    }
}
