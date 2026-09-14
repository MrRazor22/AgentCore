using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tool;
using System.Text.Json.Nodes;

namespace AgentCore.Tests;

internal static class MethodToolTestExtensions
{
    public static async Task<IReadOnlyList<IContent>> InvokeAsync(this ITool tool, JsonObject arguments, CancellationToken ct = default)
    {
        var results = new List<IContent>();
        await foreach (var evt in tool.InvokeStreamingAsync(arguments, ct))
        {
            if (evt is IContent c) results.Add(c);
        }
        return results;
    }
}

public class MethodToolTests
{
    public class SampleMethods
    {
        public int SyncAdd(int a, int b) => a + b;
        public void SyncVoid() { }
        public Task AsyncVoid() => Task.CompletedTask;
        public Task<int> AsyncAdd(int a, int b) => Task.FromResult(a + b);

        public bool InjectsToken(CancellationToken ct)
        {
            return ct != default;
        }

        public string OptionalParam(string input = "default") => input;

        public enum Mode { Fast, Slow }
        public Mode EnumParam(Mode mode) => mode;
    }

    public class BrokenMethods
    {
        public int InstanceMethod() => 42;
    }

    [Fact]
    public async Task InvokeAsync_SyncMethod_ReturnsValue()
    {
        var method = typeof(SampleMethods).GetMethod(nameof(SampleMethods.SyncAdd))!;
        var tool = new MethodTool(method, new SampleMethods());
        var args = new JsonObject { ["a"] = 2, ["b"] = 3 };

        var result = await tool.InvokeAsync(args, CancellationToken.None);

        Assert.Equal("5", ((Text)result[0]).Value);
    }

    [Fact]
    public async Task InvokeAsync_SyncVoidMethod_ReturnsNull()
    {
        var method = typeof(SampleMethods).GetMethod(nameof(SampleMethods.SyncVoid))!;
        var tool = new MethodTool(method, new SampleMethods());

        var result = await tool.InvokeAsync(new JsonObject(), CancellationToken.None);

        Assert.Equal("", ((Text)result[0]).Value);
    }

    [Fact]
    public async Task InvokeAsync_AsyncTaskMethod_AwaitsAndReturnsNull()
    {
        var method = typeof(SampleMethods).GetMethod(nameof(SampleMethods.AsyncVoid))!;
        var tool = new MethodTool(method, new SampleMethods());

        var result = await tool.InvokeAsync(new JsonObject(), CancellationToken.None);

        Assert.Equal("", ((Text)result[0]).Value);
    }

    [Fact]
    public async Task InvokeAsync_AsyncTaskOfTMethod_ReturnsValue()
    {
        var method = typeof(SampleMethods).GetMethod(nameof(SampleMethods.AsyncAdd))!;
        var tool = new MethodTool(method, new SampleMethods());
        var args = new JsonObject { ["a"] = 10, ["b"] = 20 };

        var result = await tool.InvokeAsync(args, CancellationToken.None);

        Assert.Equal("30", ((Text)result[0]).Value);
    }

    [Fact]
    public async Task InvokeAsync_InjectsCancellationToken()
    {
        var method = typeof(SampleMethods).GetMethod(nameof(SampleMethods.InjectsToken))!;
        var tool = new MethodTool(method, new SampleMethods());
        using var cts = new CancellationTokenSource();

        var result = await tool.InvokeAsync(new JsonObject(), cts.Token);

        Assert.Equal("true", ((Text)result[0]).Value);
    }

    [Fact]
    public async Task InvokeAsync_OptionalParam_UsesDefaultValue()
    {
        var method = typeof(SampleMethods).GetMethod(nameof(SampleMethods.OptionalParam))!;
        var tool = new MethodTool(method, new SampleMethods());

        // Call with empty arguments
        var resultEmpty = await tool.InvokeAsync(new JsonObject(), CancellationToken.None);
        Assert.Equal("default", ((Text)resultEmpty[0]).Value);

        // Call with explicit argument
        var args = new JsonObject { ["input"] = "custom" };
        var resultCustom = await tool.InvokeAsync(args, CancellationToken.None);
        Assert.Equal("custom", ((Text)resultCustom[0]).Value);
    }

    [Fact]
    public async Task InvokeAsync_EnumParam_DeserializesFromString()
    {
        var method = typeof(SampleMethods).GetMethod(nameof(SampleMethods.EnumParam))!;
        var tool = new MethodTool(method, new SampleMethods());
        var args = new JsonObject { ["mode"] = "Slow" };

        var result = await tool.InvokeAsync(args, CancellationToken.None);

        Assert.Equal("\"Slow\"", ((Text)result[0]).Value);
    }

    [Fact]
    public void Constructor_InstanceMethodWithoutTarget_ThrowsArgumentException()
    {
        var method = typeof(BrokenMethods).GetMethod(nameof(BrokenMethods.InstanceMethod))!;
        Assert.Throws<ArgumentException>(() => new MethodTool(method, target: null));
    }

    [Fact]
    public void Constructor_NullMethod_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MethodTool(null!));
    }
}
