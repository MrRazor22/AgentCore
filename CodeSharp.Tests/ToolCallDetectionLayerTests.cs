using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using AgentCore.Layers.LLM;
using System.Text.Json.Nodes;
using Xunit;

namespace CodeSharp.Tests;

public class ToolCallDetectionLayerTests
{
    private class MockLLM : ILLM
    {
        public IAsyncEnumerable<ILLMOutput> EmittedOutputs { get; set; } = AsyncEnumerableExtensions.ToAsyncEnumerable(Array.Empty<ILLMOutput>());

        public IAsyncEnumerable<ILLMOutput> StreamAsync(
            IReadOnlyList<Message> messages,
            JsonSchema? responseSchema = null,
            IReadOnlyList<ToolDefinition>? tools = null,
            CancellationToken ct = default)
        {
            return EmittedOutputs;
        }
    }

    private class DummyTool : Tool
    {
        public DummyTool(string name) : base(new ToolDefinition(name, "Dummy desc", new AgentCore.LLM.Schema.JsonSchema(new JsonObject())))
        {
        }

        public override Task<object?> InvokeAsync(JsonObject arguments, CancellationToken ct)
        {
            return Task.FromResult<object?>("result");
        }
    }


    private static void AttachMockInner(ToolCallDetectionLayer layer, ILLM mockInner)
    {
        var attachMethod = typeof(LLMLayer).GetMethod("Attach", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        attachMethod!.Invoke(layer, new object[] { mockInner });
    }

    [Fact]
    public async Task NativeToolCallDelta_PassesThroughUntouched()
    {
        // Arrange
        var mockLlm = new MockLLM();
        var expectedCall = new StreamChunk(new ToolCallChunk("TestTool", "{}"), Id: "call-1");
        mockLlm.EmittedOutputs = new ILLMOutput[] { expectedCall }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("TestTool").Definition }
        ).ToListAsync();

        // Assert
        var single = Assert.Single(results);
        var actualChunk = Assert.IsType<StreamChunk>(single);
        var actualCall = Assert.IsType<ToolCallChunk>(actualChunk.Content);
        Assert.Equal("TestTool", actualCall.Name);
        Assert.Equal("call-1", actualChunk.Id);
    }

    [Fact]
    public async Task TwoToolCallsInOneStream_ParsedCorrectly()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new StreamChunk(new TextChunk("<tool_call>{\"name\": \"ToolA\", \"arguments\": {}}</tool_call>\n")),
            new StreamChunk(new TextChunk("<tool_call>{\"name\": \"ToolB\", \"arguments\": {\"param\": 1}}</tool_call>"))
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("ToolA").Definition, new DummyTool("ToolB").Definition }
        ).ToListAsync();

        // Assert
        Assert.Equal(3, results.Count); // Two ToolCallChunks and a trailing newline TextChunk
        var callAChunk = Assert.IsType<StreamChunk>(results[0]);
        var callA = Assert.IsType<ToolCallChunk>(callAChunk.Content);
        Assert.Equal("ToolA", callA.Name);

        var nlChunk = Assert.IsType<StreamChunk>(results[1]);
        var nl = Assert.IsType<TextChunk>(nlChunk.Content);
        Assert.Equal("\n", nl.Text);

        var callBChunk = Assert.IsType<StreamChunk>(results[2]);
        var callB = Assert.IsType<ToolCallChunk>(callBChunk.Content);
        Assert.Equal("ToolB", callB.Name);
    }

    [Fact]
    public async Task TwoToolCallsInSingleChunk_ParsedCorrectly()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new StreamChunk(new TextChunk("<tool_call>{\"name\": \"ToolA\", \"arguments\": {}}</tool_call><tool_call>{\"name\": \"ToolB\", \"arguments\": {}}</tool_call>"))
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("ToolA").Definition, new DummyTool("ToolB").Definition }
        ).ToListAsync();

        // Assert
        Assert.Equal(2, results.Count);
        var callAChunk = Assert.IsType<StreamChunk>(results[0]);
        var callA = Assert.IsType<ToolCallChunk>(callAChunk.Content);
        Assert.Equal("ToolA", callA.Name);

        var callBChunk = Assert.IsType<StreamChunk>(results[1]);
        var callB = Assert.IsType<ToolCallChunk>(callBChunk.Content);
        Assert.Equal("ToolB", callB.Name);
    }

    [Fact]
    public async Task TextToolCallText_StreamsCorrectly()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new StreamChunk(new TextChunk("Before tool ")),
            new StreamChunk(new TextChunk("<tool_call>{\"name\": \"ToolA\", \"arguments\": {}}</tool_call>")),
            new StreamChunk(new TextChunk(" After tool"))
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("ToolA").Definition }
        ).ToListAsync();

        // Assert
        Assert.Equal(3, results.Count);
        var r0 = Assert.IsType<StreamChunk>(results[0]);
        Assert.Equal("Before tool ", Assert.IsType<TextChunk>(r0.Content).Text);

        var r1 = Assert.IsType<StreamChunk>(results[1]);
        Assert.Equal("ToolA", Assert.IsType<ToolCallChunk>(r1.Content).Name);

        var r2 = Assert.IsType<StreamChunk>(results[2]);
        Assert.Equal(" After tool", Assert.IsType<TextChunk>(r2.Content).Text);
    }

    [Fact]
    public async Task RawJsonMatchingNoRegisteredTool_PassesThroughAsText()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new StreamChunk(new TextChunk("{\"name\": \"UnregisteredTool\", \"arguments\": {}}"))
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("ToolA").Definition }
        ).ToListAsync();

        // Assert
        var single = Assert.Single(results);
        var chunk = Assert.IsType<StreamChunk>(single);
        var text = Assert.IsType<TextChunk>(chunk.Content);
        Assert.Equal("{\"name\": \"UnregisteredTool\", \"arguments\": {}}", text.Text);
    }

    [Fact]
    public async Task NonToolCallJson_DoesNotCauseExcessiveBufferingLatency()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new StreamChunk(new TextChunk("You can use a List<string> here: {\"something\": 123}"))
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("ToolA").Definition }
        ).ToListAsync();

        // Assert
        var text = string.Concat(results.OfType<StreamChunk>().Select(d => ((TextChunk)d.Content).Text));
        Assert.Contains("List<string>", text);
        Assert.Contains("{\"something\": 123}", text);
    }

    [Fact]
    public async Task ComplexEscapedQuotesAndNestedBraces_ParsedCorrectly()
    {
        // Arrange
        var mockLlm = new MockLLM();
        // Escaped string matching user request
        var rawText = "{\"name\":\"ToolA\",\"arguments\":{\"text\":\"var x = \\\"{ hello }\\\"; \\\\\\\\ test\"}}";
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new StreamChunk(new TextChunk(rawText))
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("ToolA").Definition }
        ).ToListAsync();

        // Assert
        var single = Assert.Single(results.OfType<StreamChunk>());
        var call = Assert.IsType<ToolCallChunk>(single.Content);
        Assert.Equal("ToolA", call.Name);
        Assert.Contains("hello", call.Arguments);
    }

    [Fact]
    public async Task RegisteredToolButMalformedArguments_PassesThroughOrFlushes()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new StreamChunk(new TextChunk("<tool_call>{\"name\": \"ToolA\", \"arguments\": { malformed } }</tool_call>"))
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("ToolA").Definition }
        ).ToListAsync();

        // Assert
        var text = string.Concat(results.OfType<StreamChunk>().Select(d => ((TextChunk)d.Content).Text));
        Assert.Contains("malformed", text);
    }

    [Fact]
    public async Task IncompleteCandidate_GetsReplayedUnchanged()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new StreamChunk(new TextChunk("<tool_call>{\"name\": \"ToolA\", \"arguments\": "))
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("ToolA").Definition }
        ).ToListAsync();

        // Assert
        var text = string.Concat(results.OfType<StreamChunk>().Select(d => ((TextChunk)d.Content).Text));
        Assert.Equal("<tool_call>{\"name\": \"ToolA\", \"arguments\": ", text);
    }

    [Fact]
    public async Task JsonSplitAtEveryPossibleChunkBoundary_ParsedCorrectly()
    {
        // Arrange
        var jsonStr = "<tool_call>{\"name\": \"ToolA\", \"arguments\": {\"x\": 1}}</tool_call>";
        var dummyTool = new DummyTool("ToolA");

        for (int splitIdx = 1; splitIdx < jsonStr.Length; splitIdx++)
        {
            var chunk1 = jsonStr.Substring(0, splitIdx);
            var chunk2 = jsonStr.Substring(splitIdx);

            var mockLlm = new MockLLM();
            mockLlm.EmittedOutputs = new ILLMOutput[]
            {
                new StreamChunk(new TextChunk(chunk1)),
                new StreamChunk(new TextChunk(chunk2))
            }.ToAsyncEnumerable();

            var layer = new ToolCallDetectionLayer();
            AttachMockInner(layer, mockLlm);

            // Act
            var results = await layer.StreamAsync(
                Array.Empty<Message>(),
                tools: new[] { dummyTool.Definition }
            ).ToListAsync();

            // Assert
            var single = Assert.Single(results.OfType<StreamChunk>());
            var call = Assert.IsType<ToolCallChunk>(single.Content);
            Assert.Equal("ToolA", call.Name);
        }
    }

    [Fact]
    public async Task ReasoningDelta_WhenFlushed_PreservesReasoningDeltaType()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new StreamChunk(new ReasoningChunk("Thinking process: { \"name\": \"NotATool\" }"))
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("ToolA").Definition }
        ).ToListAsync();

        // Assert
        var single = Assert.Single(results);
        var chunk = Assert.IsType<StreamChunk>(single);
        var reasoning = Assert.IsType<ReasoningChunk>(chunk.Content);
        Assert.Equal("Thinking process: { \"name\": \"NotATool\" }", reasoning.Thought);
    }

    [Fact]
    public async Task XmlTagStructuredToolCall_ParsedCorrectly()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new StreamChunk(new TextChunk("<tool_call>\n<function=TodoList>\n<parameter=todos>\n[\"ReadFile\", \"RunCommand\"]\n</parameter>\n</function>\n</tool_call>"))
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("TodoList").Definition }
        ).ToListAsync();

        // Assert
        var single = Assert.Single(results.OfType<StreamChunk>());
        var call = Assert.IsType<ToolCallChunk>(single.Content);
        Assert.Equal("TodoList", call.Name);
        Assert.Contains("ReadFile", call.Arguments);
    }

    [Fact]
    public async Task XmlTagStructuredToolCall_MultipleParameters_ParsedCorrectly()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new StreamChunk(new TextChunk("<tool_call><function=EditFile><parameter=filePath>test.txt</parameter><parameter=replacementContent>hello world</parameter></function></tool_call>"))
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("EditFile").Definition }
        ).ToListAsync();

        // Assert
        var single = Assert.Single(results.OfType<StreamChunk>());
        var call = Assert.IsType<ToolCallChunk>(single.Content);
        Assert.Equal("EditFile", call.Name);
        Assert.Contains("test.txt", call.Arguments);
        Assert.Contains("hello world", call.Arguments);
    }

    [Fact]
    public async Task XmlTagStructuredToolCall_SplitDeltas_ParsedCorrectly()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new StreamChunk(new TextChunk("<tool_call><function=TodoList>")),
            new StreamChunk(new TextChunk("<parameter=todos>[\"Search\"]</parameter>")),
            new StreamChunk(new TextChunk("</function></tool_call>"))
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("TodoList").Definition }
        ).ToListAsync();

        // Assert
        var single = Assert.Single(results.OfType<StreamChunk>());
        var call = Assert.IsType<ToolCallChunk>(single.Content);
        Assert.Equal("TodoList", call.Name);
        Assert.Contains("Search", call.Arguments);
    }

    [Fact]
    public async Task XmlTagStructuredToolCall_UnknownFunction_ReplayedUnchanged()
    {
        // Arrange
        var mockLlm = new MockLLM();
        var rawText = "<tool_call><function=FakeDangerousThing><parameter=todos>[\"Search\"]</parameter></function></tool_call>";
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new StreamChunk(new TextChunk(rawText))
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("TodoList").Definition }
        ).ToListAsync();

        // Assert
        // Should replay the unknown tool call back as raw text
        var text = string.Concat(results.OfType<StreamChunk>().Select(d => ((TextChunk)d.Content).Text));
        Assert.Equal(rawText, text);
        Assert.Empty(results.OfType<StreamChunk>().Where(d => d.Content is ToolCallChunk));
    }
}

internal static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
            await Task.CompletedTask;
        }
    }
}

internal static class ListExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
        {
            list.Add(item);
        }
        return list;
    }
}
