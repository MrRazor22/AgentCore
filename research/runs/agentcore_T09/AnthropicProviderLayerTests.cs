using System.Text.Json.Nodes;
using AgentCore;
using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using AgentCore.Tool.Tools;
using Xunit;

namespace AgentCoreT09;

public class AnthropicProviderLayerTests
{
    private sealed class MockStockTool : ITool
    {
        public ToolDefinition Definition { get; } = new(
            "lookup_stock",
            "Lookup current price of stock symbol",
            new JsonSchemaBuilder().Type<object>().AddProperty("symbol", new JsonSchemaBuilder().Type<string>().Build()).Build());

        public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
            JsonObject arguments,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            var sym = arguments["symbol"]?.ToString() ?? "UNKNOWN";
            yield return new Text($"Stock {sym}: $150.00");
        }
    }

    [Fact]
    public void Test1_MapsMessagesAndToolsToProviderSchema()
    {
        var messages = new List<Message>
        {
            new(Role.System, [new Text("You are a financial advisor.")]),
            new(Role.User, [new Text("What is AAPL price?")]),
            new(Role.Assistant, [
                new Text("Checking stock price..."),
                new ToolCall("call_123", "lookup_stock", "{\"symbol\":\"AAPL\"}")
            ]),
            new(Role.Tool, [
                new ToolResult("call_123", [new Text("Stock AAPL: $150.00")])
            ])
        };

        var tool = new MockStockTool();
        var req = AnthropicProviderLayer.FormatRequest(messages, [tool.Definition]);

        Assert.Equal("You are a financial advisor.", req["system"]?.ToString());

        var msgs = req["messages"]?.AsArray();
        Assert.NotNull(msgs);
        Assert.Equal(3, msgs.Count);

        // User turn
        Assert.Equal("user", msgs[0]?["role"]?.ToString());
        Assert.Equal("What is AAPL price?", msgs[0]?["content"]?.ToString());

        // Assistant turn with tool_use block
        Assert.Equal("assistant", msgs[1]?["role"]?.ToString());
        var assistantBlocks = msgs[1]?["content"]?.AsArray();
        Assert.NotNull(assistantBlocks);
        Assert.Equal("text", assistantBlocks[0]?["type"]?.ToString());
        Assert.Equal("tool_use", assistantBlocks[1]?["type"]?.ToString());
        Assert.Equal("lookup_stock", assistantBlocks[1]?["name"]?.ToString());

        // Tool result turn mapped to role user with type tool_result
        Assert.Equal("user", msgs[2]?["role"]?.ToString());
        var toolBlocks = msgs[2]?["content"]?.AsArray();
        Assert.NotNull(toolBlocks);
        Assert.Equal("tool_result", toolBlocks[0]?["type"]?.ToString());
        Assert.Equal("call_123", toolBlocks[0]?["tool_use_id"]?.ToString());

        // Tools schema
        var toolsArr = req["tools"]?.AsArray();
        Assert.NotNull(toolsArr);
        Assert.Single(toolsArr);
        Assert.Equal("lookup_stock", toolsArr[0]?["name"]?.ToString());
    }

    [Fact]
    public void Test2_TranslatesProviderStreamingChunks()
    {
        var msgId = "test_msg_1";

        var textChunk = new AnthropicChunk("text_delta", Text: "Hello world");
        var textEvt = AnthropicProviderLayer.TranslateStreamChunk(textChunk, msgId);
        var delta = Assert.IsType<MessageDelta>(textEvt);
        var textDelta = Assert.IsType<TextDelta>(delta.Content);
        Assert.Equal("Hello world", textDelta.Text);

        var toolChunk = new AnthropicChunk("tool_use", ToolName: "lookup_stock", ToolId: "t_1", Text: "{\"symbol\":\"MSFT\"}");
        var toolEvt = AnthropicProviderLayer.TranslateStreamChunk(toolChunk, msgId);
        var toolDelta = Assert.IsType<MessageDelta>(toolEvt);
        var tc = Assert.IsType<ToolCall>(toolDelta.Content);
        Assert.Equal("lookup_stock", tc.Name);
        Assert.Equal("t_1", tc.Id);
    }

    [Fact]
    public void Test3_HandlesProviderSpecificFinishReasonMapping()
    {
        Assert.Equal("stop", AnthropicProviderLayer.MapFinishReason("end_turn"));
        Assert.Equal("stop", AnthropicProviderLayer.MapFinishReason("stop_sequence"));
        Assert.Equal("tool_calls", AnthropicProviderLayer.MapFinishReason("tool_use"));
        Assert.Equal("length", AnthropicProviderLayer.MapFinishReason("max_tokens"));
    }

    [Fact]
    public async Task Test4_ExecutesCompleteAgentLoopWithZeroChangeToToolOrContext()
    {
        int turn = 0;
        Task<JsonObject> MockAnthropicApi(JsonObject request)
        {
            turn++;
            if (turn == 1)
            {
                var response = new JsonObject
                {
                    ["stop_reason"] = "tool_use",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = "Let me look up the stock." },
                        new JsonObject
                        {
                            ["type"] = "tool_use",
                            ["id"] = "call_stock_1",
                            ["name"] = "lookup_stock",
                            ["input"] = new JsonObject { ["symbol"] = "MSFT" }
                        }
                    }
                };
                return Task.FromResult(response);
            }
            else
            {
                var response = new JsonObject
                {
                    ["stop_reason"] = "end_turn",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = "MSFT is trading at $150.00." }
                    }
                };
                return Task.FromResult(response);
            }
        }

        var provider = new AnthropicProviderLayer(clientFn: MockAnthropicApi);
        var toolbox = new Toolbox([new MockStockTool()]);
        var context = new ChatContext();
        var agent = new Agent(provider, toolbox, context);

        var events = new List<IContentEvent>();
        await foreach (var evt in agent.InvokeStreamingAsync([new Text("Price of MSFT?")]))
        {
            events.Add(evt);
        }

        var texts = events.OfType<Text>().Select(t => t.Value).ToList();
        Assert.Contains(texts, t => t.Contains("MSFT is trading at $150.00."));

        var history = await context.ReadAsync();
        Assert.True(history.Count >= 3);
    }
}
