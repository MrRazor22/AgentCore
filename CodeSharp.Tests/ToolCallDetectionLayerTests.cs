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
        var expectedCall = new ToolCallStart("call-1", "TestTool");
        mockLlm.EmittedOutputs = new ILLMOutput[] { expectedCall }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("TestTool").Definition }
        ).ToListAsync();

        // Assert
        Assert.Single(results);
        Assert.Same(expectedCall, results[0]);
    }

    [Fact]
    public async Task TwoToolCallsInOneStream_ParsedCorrectly()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new TextDelta("<tool_call>{\"name\": \"ToolA\", \"arguments\": {}}</tool_call>\n"),
            new TextDelta("<tool_call>{\"name\": \"ToolB\", \"arguments\": {\"param\": 1}}</tool_call>")
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("ToolA").Definition, new DummyTool("ToolB").Definition }
        ).ToListAsync();

        // Assert
        var toolStarts = results.OfType<ToolCallStart>().ToList();
        Assert.Equal(2, toolStarts.Count);
        Assert.Equal("ToolA", toolStarts[0].Name);
        Assert.Equal("ToolB", toolStarts[1].Name);
    }

    [Fact]
    public async Task TwoToolCallsInSingleChunk_ParsedCorrectly()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new TextDelta("<tool_call>{\"name\": \"ToolA\", \"arguments\": {}}</tool_call><tool_call>{\"name\": \"ToolB\", \"arguments\": {}}</tool_call>")
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("ToolA").Definition, new DummyTool("ToolB").Definition }
        ).ToListAsync();

        // Assert
        var toolStarts = results.OfType<ToolCallStart>().ToList();
        Assert.Equal(2, toolStarts.Count);
        Assert.Equal("ToolA", toolStarts[0].Name);
        Assert.Equal("ToolB", toolStarts[1].Name);
    }

    [Fact]
    public async Task TextToolCallText_StreamsCorrectly()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new TextDelta("Before tool "),
            new TextDelta("<tool_call>{\"name\": \"ToolA\", \"arguments\": {}}</tool_call>"),
            new TextDelta(" After tool")
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("ToolA").Definition }
        ).ToListAsync();

        // Assert
        var textDeltas = results.OfType<TextDelta>().ToList();
        Assert.Equal(2, textDeltas.Count);
        Assert.Equal("Before tool ", textDeltas[0].Text);
        Assert.Equal(" After tool", textDeltas[1].Text);

        var toolStart = Assert.Single(results.OfType<ToolCallStart>());
        Assert.Equal("ToolA", toolStart.Name);
    }

    [Fact]
    public async Task RawJsonMatchingNoRegisteredTool_PassesThroughAsText()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new TextDelta("{\"name\": \"UnregisteredTool\", \"arguments\": {}}")
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
        var text = Assert.IsType<TextDelta>(single);
        Assert.Equal("{\"name\": \"UnregisteredTool\", \"arguments\": {}}", text.Text);
    }

    [Fact]
    public async Task NonToolCallJson_DoesNotCauseExcessiveBufferingLatency()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new TextDelta("You can use a List<string> here: {\"something\": 123}")
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("ToolA").Definition }
        ).ToListAsync();

        // Assert
        var text = string.Concat(results.OfType<TextDelta>().Select(d => d.Text));
        Assert.Contains("List<string>", text);
        Assert.Contains("{\"something\": 123}", text);
    }

    [Fact]
    public async Task ComplexEscapedQuotesAndNestedBraces_ParsedCorrectly()
    {
        // Arrange
        var mockLlm = new MockLLM();
        var rawText = "{\"name\":\"ToolA\",\"arguments\":{\"text\":\"var x = \\\"{ hello }\\\"; \\\\\\\\ test\"}}";
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new TextDelta(rawText)
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("ToolA").Definition }
        ).ToListAsync();

        // Assert
        var call = Assert.Single(results.OfType<ToolCallStart>());
        Assert.Equal("ToolA", call.Name);
        var delta = Assert.Single(results.OfType<ToolCallDelta>());
        Assert.Contains("hello", delta.Arguments);
    }

    [Fact]
    public async Task RegisteredToolButMalformedArguments_PassesThroughOrFlushes()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new TextDelta("<tool_call>{\"name\": \"ToolA\", \"arguments\": { malformed } }</tool_call>")
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("ToolA").Definition }
        ).ToListAsync();

        // Assert
        var text = string.Concat(results.OfType<TextDelta>().Select(d => d.Text));
        Assert.Contains("malformed", text);
    }

    [Fact]
    public async Task IncompleteCandidate_GetsReplayedUnchanged()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new TextDelta("<tool_call>{\"name\": \"ToolA\", \"arguments\": ")
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("ToolA").Definition }
        ).ToListAsync();

        // Assert
        var text = string.Concat(results.OfType<TextDelta>().Select(d => d.Text));
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
                new TextDelta(chunk1),
                new TextDelta(chunk2)
            }.ToAsyncEnumerable();

            var layer = new ToolCallDetectionLayer();
            AttachMockInner(layer, mockLlm);

            // Act
            var results = await layer.StreamAsync(
                Array.Empty<Message>(),
                tools: new[] { dummyTool.Definition }
            ).ToListAsync();

            // Assert
            var call = Assert.Single(results.OfType<ToolCallStart>());
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
            new ReasoningDelta("Thinking process: { \"name\": \"NotATool\" }")
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
        var reasoning = Assert.IsType<ReasoningDelta>(single);
        Assert.Equal("Thinking process: { \"name\": \"NotATool\" }", reasoning.Thought);
    }

    [Fact]
    public async Task XmlTagStructuredToolCall_ParsedCorrectly()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new TextDelta("<tool_call>\n<function=TodoList>\n<parameter=todos>\n[\"ReadFile\", \"RunCommand\"]\n</parameter>\n</function>\n</tool_call>")
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("TodoList").Definition }
        ).ToListAsync();

        // Assert
        var call = Assert.Single(results.OfType<ToolCallStart>());
        Assert.Equal("TodoList", call.Name);
        var delta = Assert.Single(results.OfType<ToolCallDelta>());
        Assert.Contains("ReadFile", delta.Arguments);
    }

    [Fact]
    public async Task XmlTagStructuredToolCall_MultipleParameters_ParsedCorrectly()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new TextDelta("<tool_call><function=EditFile><parameter=filePath>test.txt</parameter><parameter=replacementContent>hello world</parameter></function></tool_call>")
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("EditFile").Definition }
        ).ToListAsync();

        // Assert
        var call = Assert.Single(results.OfType<ToolCallStart>());
        Assert.Equal("EditFile", call.Name);
        var delta = Assert.Single(results.OfType<ToolCallDelta>());
        Assert.Contains("test.txt", delta.Arguments);
        Assert.Contains("hello world", delta.Arguments);
    }

    [Fact]
    public async Task XmlTagStructuredToolCall_SplitDeltas_ParsedCorrectly()
    {
        // Arrange
        var mockLlm = new MockLLM();
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new TextDelta("<tool_call><function=TodoList>"),
            new TextDelta("<parameter=todos>[\"Search\"]</parameter>"),
            new TextDelta("</function></tool_call>")
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("TodoList").Definition }
        ).ToListAsync();

        // Assert
        var call = Assert.Single(results.OfType<ToolCallStart>());
        Assert.Equal("TodoList", call.Name);
        var delta = Assert.Single(results.OfType<ToolCallDelta>());
        Assert.Contains("Search", delta.Arguments);
    }

    [Fact]
    public async Task XmlTagStructuredToolCall_UnknownFunction_ReplayedUnchanged()
    {
        // Arrange
        var mockLlm = new MockLLM();
        var rawText = "<tool_call><function=FakeDangerousThing><parameter=todos>[\"Search\"]</parameter></function></tool_call>";
        mockLlm.EmittedOutputs = new ILLMOutput[]
        {
            new TextDelta(rawText)
        }.ToAsyncEnumerable();

        var layer = new ToolCallDetectionLayer();
        AttachMockInner(layer, mockLlm);

        // Act
        var results = await layer.StreamAsync(
            Array.Empty<Message>(),
            tools: new[] { new DummyTool("TodoList").Definition }
        ).ToListAsync();

        // Assert
        var text = string.Concat(results.OfType<TextDelta>().Select(d => d.Text));
        Assert.Equal(rawText, text);
        Assert.Empty(results.OfType<ToolCallStart>());
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
