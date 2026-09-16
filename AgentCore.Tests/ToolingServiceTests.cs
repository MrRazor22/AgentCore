using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tooling;
using System.Text.Json.Nodes;

namespace AgentCore.Tests;

public class ToolingServiceTests
{
    private class FakeTool(string name, JsonSchema schema) : ITool
    {
        public ToolDefinition Info { get; } = new(name, "Fake Description", schema);

        public Func<JsonObject, CancellationToken, Task<IReadOnlyList<IContent>>> Invoker { get; set; } =
            (args, ct) => Task.FromResult<IReadOnlyList<IContent>>([new Text("Result")]);

        public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
            JsonObject arguments,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var contents = await Invoker(arguments, ct);
            foreach (var c in contents)
            {
                yield return c;
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnregisteredToolName_ReturnsErrorMessage()
    {
        var tooling = new Toolbox(Array.Empty<ITool>());
        var call = new ToolCall("call_1", "missing_tool");

        var result = await tooling.ExecuteAsync(call);

        var resultText = result.ToString();
        Assert.Contains("not registered", resultText);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyToolName_ReturnsErrorMessage()
    {
        var tooling = new Toolbox(Array.Empty<ITool>());
        var call = new ToolCall("call_1", "");

        var result = await tooling.ExecuteAsync(call);

        var resultText = result.ToString();
        Assert.Contains("cannot be empty", resultText);
    }

    [Fact]
    public async Task ExecuteAsync_ToolThrowsException_ReturnsErrorMessageNotException()
    {
        var schema = new LLM.Schema.JsonSchemaBuilder().Type<object>().Build();
        var tool = new FakeTool("crash_tool", schema)
        {
            Invoker = (args, ct) => throw new InvalidOperationException("Tool implementation crashed")
        };
        var tooling = new Toolbox(new[] { tool });

        var call = new ToolCall("call_1", "crash_tool");

        var result = await tooling.ExecuteAsync(call);

        // Should NOT throw, but return error text
        var resultText = result.ToString();
        Assert.Contains("Tool implementation crashed", resultText);
    }

    [Fact]
    public async Task ExecuteAsync_ToolReturnsNull_ReturnsEmptyText()
    {
        var schema = new LLM.Schema.JsonSchemaBuilder().Type<object>().Build();
        var tool = new FakeTool("null_tool", schema) { Invoker = (args, ct) => Task.FromResult<IReadOnlyList<IContent>>(Array.Empty<IContent>()) };
        var tooling = new Toolbox(new[] { tool });

        var call = new ToolCall("call_1", "null_tool");
        var result = await tooling.ExecuteAsync(call);

        var resultText = result.ToString();
        Assert.Equal("", resultText);
    }

    [Fact]
    public async Task ExecuteAsync_ToolReturnsIContent_UsedDirectly()
    {
        var schema = new LLM.Schema.JsonSchemaBuilder().Type<object>().Build();
        var tool = new FakeTool("content_tool", schema)
        {
            Invoker = (args, ct) => Task.FromResult<IReadOnlyList<IContent>>([new Text("Explicit IContent")])
        };
        var tooling = new Toolbox(new[] { tool });

        var call = new ToolCall("call_1", "content_tool");
        var result = await tooling.ExecuteAsync(call);

        var resultText = result.ToString();
        Assert.Equal("Explicit IContent", resultText);
    }

    [Fact]
    public async Task ExecuteAsync_ToolReturnsObject_JsonSerialized()
    {
        var schema = new LLM.Schema.JsonSchemaBuilder().Type<object>().Build();
        var tool = new FakeTool("object_tool", schema)
        {
            Invoker = (args, ct) => Task.FromResult<IReadOnlyList<IContent>>([new Text("{\"Key\":\"Val\"}")])
        };
        var tooling = new Toolbox(new[] { tool });

        var call = new ToolCall("call_1", "object_tool");
        var result = await tooling.ExecuteAsync(call);

        var resultText = result.ToString();
        Assert.Contains("{\"Key\":\"Val\"}", resultText);
    }

    [Fact]
    public async Task ExecuteAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        var schema = new LLM.Schema.JsonSchemaBuilder().Type<object>().Build();
        var tool = new FakeTool("slow_tool", schema)
        {
            Invoker = async (args, ct) => { await Task.Delay(5000, ct); return [new Text("Done")]; }
        };
        var tooling = new Toolbox(new[] { tool });

        var call = new ToolCall("call_1", "slow_tool");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await tooling.ExecuteAsync(call, cts.Token);
        });
    }
}
