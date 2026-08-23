using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Chat.Builders;
using Xunit;

namespace AgentCore.Tests;

public class MessageAssemblyTests
{
    private static async IAsyncEnumerable<T> ToAsyncStream<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
        await Task.CompletedTask;
    }


    [Fact]
    public async Task AssembleAsync_IndexAndIdPermutations_MergesCorrectly()
    {
        // RAW stream sequence 1: Index + ID -> later ID only
        var sequence1 = new List<ILLMOutput>
        {
            new StreamChunk(new ToolCallChunk("RunCommand", ""), Index: 0, Id: "ABC"),
            new StreamChunk(new ToolCallChunk(null, "{\"commandLine\":\"ls\"}"), Id: "ABC")
        };

        var (contents1, _, _) = await ToAsyncStream(sequence1).AssembleAsync();
        Assert.NotNull(contents1);
        var calls1 = contents1.OfType<ToolCall>().ToList();
        Assert.Single(calls1);
        Assert.Equal("ABC", calls1[0].Id);
        Assert.Equal("RunCommand", calls1[0].Name);
        Assert.Contains("ls", calls1[0].Arguments.ToString());

        // RAW stream sequence 2: ID only -> later Index + ID
        var sequence2 = new List<ILLMOutput>
        {
            new StreamChunk(new ToolCallChunk("SearchWeb", ""), Id: "XYZ"),
            new StreamChunk(new ToolCallChunk(null, "{\"query\":\"test\"}"), Index: 1, Id: "XYZ")
        };

        var (contents2, _, _) = await ToAsyncStream(sequence2).AssembleAsync();
        Assert.NotNull(contents2);
        var calls2 = contents2.OfType<ToolCall>().ToList();
        Assert.Single(calls2);
        Assert.Equal("XYZ", calls2[0].Id);
        Assert.Equal("SearchWeb", calls2[0].Name);
        Assert.Contains("test", calls2[0].Arguments.ToString());
    }

    [Fact]
    public async Task AssembleAsync_IndexAndId_ReconcilesWithoutIdDuplication()
    {
        var sequence = new List<ILLMOutput>
        {
            new StreamChunk(new ToolCallChunk("RunCommand", ""), Index: 0, Id: "ABC"),
            new StreamChunk(new ToolCallChunk(null, "{\"commandLine\":\"Get-ChildItem -Path $PWD\",\"outputCharacterCount\":2000}"), Index: 0, Id: ""),
            new StreamChunk(new ToolCallChunk("RunCommand", "{\"commandLine\":\"Get-ChildItem -Path $PWD\",\"outputCharacterCount\":2000}"), Id: "ABC")
        };

        var (contents, _, _) = await ToAsyncStream(sequence).AssembleAsync();
        Assert.NotNull(contents);
        var calls = contents.OfType<ToolCall>().ToList();
        Assert.Single(calls);
        Assert.Equal("ABC", calls[0].Id);
        Assert.Equal("RunCommand", calls[0].Name);
    }

    [Fact]
    public async Task AssembleAsync_MultipleSimultaneousInterleavedCalls_ResolvesCorrectly()
    {
        var sequence = new List<ILLMOutput>
        {
            new StreamChunk(new ToolCallChunk("RunCommand", ""), Index: 0, Id: "A"),
            new StreamChunk(new ToolCallChunk("SearchWeb", ""), Index: 1, Id: "B"),
            new StreamChunk(new ToolCallChunk(null, "{\"commandLine\":"), Index: 0, Id: ""),
            new StreamChunk(new ToolCallChunk(null, "{\"query\":"), Index: 1, Id: ""),
            new StreamChunk(new ToolCallChunk(null, "\"ls\"}"), Index: 0, Id: ""),
            new StreamChunk(new ToolCallChunk(null, "\"test\"}"), Index: 1, Id: "")
        };

        var (contents, _, _) = await ToAsyncStream(sequence).AssembleAsync();
        Assert.NotNull(contents);
        var calls = contents.OfType<ToolCall>().ToList();

        Assert.Equal(2, calls.Count);

        Assert.Equal("A", calls[0].Id);
        Assert.Equal("RunCommand", calls[0].Name);
        Assert.Contains("ls", calls[0].Arguments.ToString());

        Assert.Equal("B", calls[1].Id);
        Assert.Equal("SearchWeb", calls[1].Name);
        Assert.Contains("test", calls[1].Arguments.ToString());
    }

    [Fact]
    public async Task AssembleAsync_AmbiguousDeltaWithMultipleActiveGroups_ThrowsInvalidOperationException()
    {
        var sequence = new List<ILLMOutput>
        {
            new StreamChunk(new ToolCallChunk("RunCommand", ""), Index: 0, Id: "A"),
            new StreamChunk(new ToolCallChunk("SearchWeb", ""), Index: 1, Id: "B"),
            new StreamChunk(new ToolCallChunk(null, "{\"commandLine\":\"ls\"}"))
        };

        await Assert.ThrowsAsync<System.InvalidOperationException>(async () =>
        {
            await ToAsyncStream(sequence).AssembleAsync();
        });
    }

    [Fact]
    public async Task AssembleAsync_OnCancellation_ReturnsAccumulatedTextAndReasoning()
    {
        var cts = new System.Threading.CancellationTokenSource();

        async IAsyncEnumerable<ILLMOutput> CancellationStream([System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct = default)
        {
            yield return new StreamChunk(new ReasoningChunk("Thinking deeply..."));
            yield return new StreamChunk(new TextChunk("Hello "));
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            yield return new StreamChunk(new TextChunk("Unreachable text"));
        }

        var (contents, _, _) = await CancellationStream().AssembleAsync(cts.Token);

        Assert.NotNull(contents);
        Assert.Equal(2, contents.Count);

        var reasoning = contents.OfType<Reasoning>().FirstOrDefault();
        Assert.NotNull(reasoning);
        Assert.Equal("Thinking deeply...", reasoning.Thought);

        var text = contents.OfType<Text>().FirstOrDefault();
        Assert.NotNull(text);
        Assert.Equal("Hello", text.Value);
    }

    [Fact]
    public async Task AssembleAsync_OnCancellation_DiscardsNamelessToolCalls()
    {
        var cts = new System.Threading.CancellationTokenSource();

        async IAsyncEnumerable<ILLMOutput> CancellationStream([System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct = default)
        {
            yield return new StreamChunk(new TextChunk("Calling tool "));
            yield return new StreamChunk(new ToolCallChunk("", "{\"query\": \"incomp\""), Index: 0, Id: "TC1");
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
        }

        var (contents, _, _) = await CancellationStream().AssembleAsync(cts.Token);

        Assert.NotNull(contents);
        Assert.Single(contents);
        var text = Assert.IsType<Text>(contents[0]);
        Assert.Equal("Calling tool", text.Value);
        Assert.Empty(contents.OfType<ToolCall>());
    }

    [Fact]
    public async Task AssembleAsync_OnCancellationWithNoContent_ThrowsCancellation()
    {
        var cts = new System.Threading.CancellationTokenSource();
        await cts.CancelAsync();

        async IAsyncEnumerable<ILLMOutput> EmptyStream([System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            yield break;
        }

        await Assert.ThrowsAsync<System.OperationCanceledException>(async () =>
        {
            await EmptyStream().AssembleAsync(cts.Token);
        });
    }

    [Fact]
    public async Task AssembleAsync_OnEmptyNormalCompletion_ThrowsInvalidOperationException()
    {
        async IAsyncEnumerable<ILLMOutput> EmptyStream()
        {
            await Task.Yield();
            yield break;
        }

        await Assert.ThrowsAsync<System.InvalidOperationException>(async () =>
        {
            await EmptyStream().AssembleAsync();
        });
    }
    [Fact]
    public void Message_Receive_FluidAndStructuralStreaming_BehavesCorrectly()
    {
        var message = new Message(Role.Assistant);
        var r1 = message.Receive(new StreamChunk(new ReasoningChunk("Thinking "), IsFinal: false)).ToList();
        Assert.Empty(r1);

        var r2 = message.Receive(new StreamChunk(new ReasoningChunk("deeply..."), IsFinal: true)).ToList();
        Assert.Single(r2);
        Assert.Equal("Thinking deeply...", Assert.IsType<Reasoning>(r2[0]).Thought);

        var r3 = message.Receive(new StreamChunk(new TextChunk("Here is the answer."), IsFinal: true)).ToList();
        Assert.Single(r3);
        Assert.Equal("Here is the answer.", Assert.IsType<Text>(r3[0]).Value);

        Assert.Equal(2, message.Contents.Count);
        Assert.Equal("Thinking deeply...", Assert.IsType<Reasoning>(message.Contents[0]).Thought);
        Assert.Equal("Here is the answer.", Assert.IsType<Text>(message.Contents[1]).Value);
    }

    [Fact]
    public void Message_Receive_MultiBoundaryTransition_YieldsSettledContentsInOrder()
    {
        var message = new Message(Role.Assistant);

        var r1 = message.Receive(new StreamChunk(new ReasoningChunk("Step 1: Analyze"), IsFinal: true)).ToList();
        Assert.Single(r1);
        Assert.Equal("Step 1: Analyze", Assert.IsType<Reasoning>(r1[0]).Thought);

        // ToolCall JSON streaming
        var r2 = message.Receive(new StreamChunk(new ToolCallChunk("lookup", "{\"q\":"), Id: "call_1", IsFinal: false)).ToList();
        Assert.Empty(r2);

        // ToolCall JSON completes with IsFinal -> ToolCall emitted
        var r3 = message.Receive(new StreamChunk(new ToolCallChunk(null, "\"test\"}"), Id: "call_1", IsFinal: true)).ToList();
        Assert.Single(r3);
        var tc = Assert.IsType<ToolCall>(r3[0]);
        Assert.Equal("call_1", tc.Id);
        Assert.Equal("lookup", tc.Name);

        // Text streaming
        var r4 = message.Receive(new StreamChunk(new TextChunk("The result is ready."), IsFinal: true)).ToList();
        Assert.Single(r4);
        Assert.Equal("The result is ready.", Assert.IsType<Text>(r4[0]).Value);

        Assert.Equal(3, message.Contents.Count);
        Assert.IsType<Reasoning>(message.Contents[0]);
        Assert.IsType<ToolCall>(message.Contents[1]);
        Assert.IsType<Text>(message.Contents[2]);
    }

    [Fact]
    public void ContentAssembler_InterleavedTextAndReasoning_MaintainsIndependentBuffers()
    {
        var assembler = new AgentCore.LLM.Chat.Builders.ContentAssembler();

        // Interleave Text A and Reasoning B
        var y1 = assembler.Receive(new StreamChunk(new TextChunk("Hello "), Id: "stream_text", IsFinal: false)).ToList();
        Assert.Empty(y1);

        var y2 = assembler.Receive(new StreamChunk(new ReasoningChunk("Thinking "), Id: "stream_thought", IsFinal: false)).ToList();
        Assert.Empty(y2);

        var y3 = assembler.Receive(new StreamChunk(new TextChunk("world!"), Id: "stream_text", IsFinal: true)).ToList();
        Assert.Single(y3);
        Assert.Equal("Hello world!", Assert.IsType<Text>(y3[0]).Value);

        // Thought stream continues after Text stream finished!
        var y4 = assembler.Receive(new StreamChunk(new ReasoningChunk("more..."), Id: "stream_thought", IsFinal: true)).ToList();
        Assert.Single(y4);
        Assert.Equal("Thinking more...", Assert.IsType<Reasoning>(y4[0]).Thought);
    }

    [Fact]
    public void ContentAssembler_ThreeParallelToolCalls_EmitsEarlyFinalizedCallFirst()
    {
        var assembler = new AgentCore.LLM.Chat.Builders.ContentAssembler();

        // Start 3 parallel tool calls
        assembler.Receive(new StreamChunk(new ToolCallChunk("ToolA", "{\"a\":"), Index: 0, Id: "call_A", IsFinal: false)).ToList();
        assembler.Receive(new StreamChunk(new ToolCallChunk("ToolB", "{\"b\":"), Index: 1, Id: "call_B", IsFinal: false)).ToList();
        assembler.Receive(new StreamChunk(new ToolCallChunk("ToolC", "{\"c\":"), Index: 2, Id: "call_C", IsFinal: false)).ToList();

        // ToolCall B finishes FIRST
        var bFinish = assembler.Receive(new StreamChunk(new ToolCallChunk(null, "2}"), Index: 1, Id: "call_B", IsFinal: true)).ToList();
        Assert.Single(bFinish);
        var tcB = Assert.IsType<ToolCall>(bFinish[0]);
        Assert.Equal("call_B", tcB.Id);
        Assert.Equal("ToolB", tcB.Name);

        // ToolCall A and C continue accumulation
        assembler.Receive(new StreamChunk(new ToolCallChunk(null, "1}"), Index: 0, Id: "call_A", IsFinal: false)).ToList();
        assembler.Receive(new StreamChunk(new ToolCallChunk(null, "3}"), Index: 2, Id: "call_C", IsFinal: false)).ToList();

        // ToolCall C finishes SECOND
        var cFinish = assembler.Receive(new StreamChunk(new ToolCallChunk(null, ""), Index: 2, Id: "call_C", IsFinal: true)).ToList();
        Assert.Single(cFinish);
        Assert.Equal("call_C", Assert.IsType<ToolCall>(cFinish[0]).Id);

        // ToolCall A finishes LAST
        var aFinish = assembler.Receive(new StreamChunk(new ToolCallChunk(null, ""), Index: 0, Id: "call_A", IsFinal: true)).ToList();
        Assert.Single(aFinish);
        Assert.Equal("call_A", Assert.IsType<ToolCall>(aFinish[0]).Id);
    }

    [Fact]
    public void ContentAssembler_ZeroJsonShapeHeuristics_OnlyEmitsOnIsFinal()
    {
        var assembler = new AgentCore.LLM.Chat.Builders.ContentAssembler();

        // Send a complete JSON string but with IsFinal: false -> MUST NOT EMIT!
        var y1 = assembler.Receive(new StreamChunk(new ToolCallChunk("lookup", "{\"valid\":\"json\"}"), Id: "call_1", IsFinal: false)).ToList();
        Assert.Empty(y1);

        // Now send IsFinal: true -> EMITS!
        var y2 = assembler.Receive(new StreamChunk(new ToolCallChunk(null, ""), Id: "call_1", IsFinal: true)).ToList();
        Assert.Single(y2);
        var tc = Assert.IsType<ToolCall>(y2[0]);
        Assert.Equal("call_1", tc.Id);
        Assert.Equal("lookup", tc.Name);
    }

    private record TestImageChunk(string Base64Data, string MimeType) : IContentChunk;
    private record TestImageContent(string Data, string MimeType) : IContent
    {
        public string ForLlm() => $"[Image: {MimeType}]";
    }

    private class TestImageBuilder : IContentBuilder
    {
        private readonly System.Text.StringBuilder _data = new();
        private string _mimeType = "image/png";

        public IEnumerable<IContent> Append(StreamChunk chunk)
        {
            if (chunk.Content is TestImageChunk img)
            {
                _data.Append(img.Base64Data);
                if (!string.IsNullOrEmpty(img.MimeType)) _mimeType = img.MimeType;
            }

            if (chunk.IsFinal)
            {
                yield return new TestImageContent(_data.ToString(), _mimeType);
            }
        }
    }

    [Fact]
    public void ContentAssembler_MultimodalExtensibility_SupportsCustomChunkAndBuilderRegistration()
    {
        var assembler = new AgentCore.LLM.Chat.Builders.ContentAssembler()
            .RegisterBuilder<TestImageChunk>(() => new TestImageBuilder());

        // Stream image chunks
        var r1 = assembler.Receive(new StreamChunk(new TestImageChunk("iVBORw0K", "image/png"), Id: "img_1", IsFinal: false)).ToList();
        Assert.Empty(r1);

        var r2 = assembler.Receive(new StreamChunk(new TestImageChunk("GgoAAAANSUhEUg==", "image/png"), Id: "img_1", IsFinal: true)).ToList();
        Assert.Single(r2);

        var imgContent = Assert.IsType<TestImageContent>(r2[0]);
        Assert.Equal("iVBORw0KGgoAAAANSUhEUg==", imgContent.Data);
        Assert.Equal("image/png", imgContent.MimeType);
        Assert.Equal("[Image: image/png]", imgContent.ForLlm());
    }

    [Fact]
    public void Message_Serialization_SerializesCommittedContents()
    {
        var message = new Message(Role.Assistant, new Text("Committed message"));

        var json = System.Text.Json.JsonSerializer.Serialize(message);
        Assert.Contains("\"role\":\"Assistant\"", json);
        Assert.Contains("Committed message", json);
    }

    [Fact]
    public void Message_Constructor_InitializesContentsCorrectly()
    {
        var message = new Message(
            Role.Assistant,
            new Text("Hello"),
            new ToolResult("call_1", new Text("Done")));

        Assert.Equal(2, message.Contents.Count);
        Assert.Equal("Hello", Assert.IsType<Text>(message.Contents[0]).Value);
        Assert.Equal("call_1", Assert.IsType<ToolResult>(message.Contents[1]).CallId);
    }
}

internal static class TestMessageAssemblyExtensions
{
    private static List<IContent> Consolidate(IReadOnlyList<IContent> items)
    {
        var result = new List<IContent>();
        foreach (var item in items)
        {
            switch (item)
            {
                case Text t:
                    var tStr = t.Value.Trim();
                    if (!string.IsNullOrEmpty(tStr)) result.Add(new Text(tStr));
                    break;
                case Reasoning r:
                    var rStr = r.Thought.Trim();
                    if (!string.IsNullOrEmpty(rStr)) result.Add(new Reasoning(rStr));
                    break;
                case ToolCall tc:
                    if (!string.IsNullOrEmpty(tc.Name)) result.Add(tc);
                    break;
                default:
                    result.Add(item);
                    break;
            }
        }
        return result;
    }

    public static async Task<(IReadOnlyList<IContent> Contents, TokenUsage? TokenUsage, FinishReason? FinishReason)> AssembleAsync(
        this IAsyncEnumerable<ILLMOutput> stream,
        CancellationToken ct = default)
    {
        var message = new Message(Role.Assistant);
        TokenUsage? tokenUsage = null;
        FinishReason? finishReason = null;
        Exception? caughtException = null;

        var items = new List<ILLMOutput>();
        try
        {
            await foreach (var item in stream.WithCancellation(ct).ConfigureAwait(false))
            {
                items.Add(item);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException || ex is System.IO.IOException || ex is System.Net.Http.HttpRequestException)
        {
            caughtException = ex;
        }

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            switch (item)
            {
                case StreamChunk chunk:
                    bool hasSubsequentSameStream = items.Skip(i + 1).OfType<StreamChunk>().Any(next =>
                    {
                        if (!string.IsNullOrEmpty(chunk.Id) && !string.IsNullOrEmpty(next.Id))
                            return string.Equals(chunk.Id, next.Id, StringComparison.Ordinal);
                        if (chunk.Index.HasValue && next.Index.HasValue)
                            return chunk.Index.Value == next.Index.Value;
                        return chunk.Content.GetType() == next.Content.GetType();
                    });

                    var finalChunk = chunk with { IsFinal = chunk.IsFinal || !hasSubsequentSameStream };
                    foreach (var content in message.Receive(finalChunk)) { }
                    break;


                case TokenUsage tu:
                    tokenUsage = tu;
                    break;

                case FinishReason fr:
                    finishReason = fr;
                    break;
            }
        }

        var consolidated = Consolidate(message.Contents);

        if (consolidated.Count == 0)
        {
            if (caughtException != null)
            {
                throw caughtException;
            }
            throw new InvalidOperationException("LLM returned an empty assistant response.");
        }

        return (consolidated, tokenUsage, finishReason);
    }
}






