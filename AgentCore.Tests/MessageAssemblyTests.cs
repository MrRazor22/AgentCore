using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using Xunit;

namespace AgentCore.Tests;

public class MessageAssemblyTests
{
    private static async IAsyncEnumerable<ILLMOutput> ToAsyncStream(IEnumerable<ILLMOutput> items)
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
            new ToolCallDelta("ABC", "RunCommand", "", 0),
            new ToolCallDelta("ABC", null, "{\"commandLine\":\"ls\"}", null)
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
            new ToolCallDelta("XYZ", "SearchWeb", "", null),
            new ToolCallDelta("XYZ", null, "{\"query\":\"test\"}", 1)
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
            new ToolCallDelta("ABC", "RunCommand", "", 0),
            new ToolCallDelta("", null, "{\"commandLine\":\"Get-ChildItem -Path $PWD\",\"outputCharacterCount\":2000}", 0),
            new ToolCallDelta("ABC", "RunCommand", "{\"commandLine\":\"Get-ChildItem -Path $PWD\",\"outputCharacterCount\":2000}", null)
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
            new ToolCallDelta("A", "RunCommand", "", 0),
            new ToolCallDelta("B", "SearchWeb", "", 1),
            new ToolCallDelta("", null, "{\"commandLine\":", 0),
            new ToolCallDelta("", null, "{\"query\":", 1),
            new ToolCallDelta("", null, "\"ls\"}", 0),
            new ToolCallDelta("", null, "\"test\"}", 1)
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
            new ToolCallDelta("A", "RunCommand", "", 0),
            new ToolCallDelta("B", "SearchWeb", "", 1),
            new ToolCallDelta("", null, "{\"commandLine\":\"ls\"}", null)
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
            yield return new ReasoningDelta("Thinking deeply...");
            yield return new TextDelta("Hello ");
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            yield return new TextDelta("Unreachable text");
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
            yield return new TextDelta("Calling tool ");
            yield return new ToolCallDelta("TC1", "", "{\"query\": \"incomp\"", 0);
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
    public void Message_Contents_MidStream_ReturnsSnapshotWithoutDisruptingStream()
    {
        var message = new Message(Role.Assistant);
        Assert.Empty(message.Contents);

        var snap1 = message.AddContentDelta(new TextDelta("Hello "));
        Assert.Empty(message.Contents);
        Assert.Single(snap1);
        Assert.Equal("Hello ", Assert.IsType<Text>(snap1[0]).Value);

        // Stream continues with the same active builder
        var snap2 = message.AddContentDelta(new TextDelta("world!"));
        Assert.Empty(message.Contents);
        Assert.Single(snap2);
        Assert.Equal("Hello world!", Assert.IsType<Text>(snap2[0]).Value);
    }

    [Fact]
    public void Message_AddContentDelta_BoundaryTransition_CommitsToContents()
    {
        var message = new Message(Role.Assistant);

        var snap1 = message.AddContentDelta(new ReasoningDelta("Thinking deeply..."));
        Assert.Single(snap1);
        Assert.Equal("Thinking deeply...", Assert.IsType<Reasoning>(snap1[0]).Thought);
        Assert.Empty(message.Contents);

        // Boundary switch: ReasoningDelta -> TextDelta
        var snap2 = message.AddContentDelta(new TextDelta("Here is the answer."));
        Assert.Equal(2, snap2.Count);
        Assert.Equal("Thinking deeply...", Assert.IsType<Reasoning>(snap2[0]).Thought);
        Assert.Equal("Here is the answer.", Assert.IsType<Text>(snap2[1]).Value);

        // Contents snapshot includes only committed Reasoning
        Assert.Single(message.Contents);
        Assert.Equal("Thinking deeply...", Assert.IsType<Reasoning>(message.Contents[0]).Thought);
    }

    [Fact]
    public void Message_Serialization_SerializesCommittedContents()
    {
        var message = new Message(Role.Assistant);
        message.AddContent(new Text("Committed message"));

        var json = System.Text.Json.JsonSerializer.Serialize(message);
        Assert.Contains("\"role\":\"Assistant\"", json);
        Assert.Contains("Committed message", json);
    }

    [Fact]
    public void Message_AddContent_FluentChaining_AppendsSettledContent()
    {
        var message = new Message(Role.Assistant)
            .AddContent(new Text("Hello"))
            .AddContent(new ToolResult("call_1", new Text("Done")));

        Assert.Equal(2, message.Contents.Count);
        Assert.Equal("Hello", Assert.IsType<Text>(message.Contents[0]).Value);
        Assert.Equal("call_1", Assert.IsType<ToolResult>(message.Contents[1]).CallId);
    }

    [Fact]
    public void Message_AddContent_WithActiveStreamingBuilder_CommitsActiveContentFirst()
    {
        var message = new Message(Role.Assistant);
        message.AddContentDelta(new TextDelta("Streaming start..."));

        message.AddContent(new ToolResult("call_1", new Text("Settled result")));

        Assert.Equal(2, message.Contents.Count);
        Assert.Equal("Streaming start...", Assert.IsType<Text>(message.Contents[0]).Value);
        Assert.Equal("call_1", Assert.IsType<ToolResult>(message.Contents[1]).CallId);
    }

    [Fact]
    public void Message_AddContent_Null_ThrowsArgumentNullException()
    {
        var message = new Message(Role.Assistant);
        Assert.Throws<ArgumentNullException>(() => message.AddContent(null!));
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
        IReadOnlyList<IContent> lastSnapshot = [];
        TokenUsage? tokenUsage = null;
        FinishReason? finishReason = null;
        Exception? caughtException = null;

        try
        {
            await foreach (var item in stream.WithCancellation(ct).ConfigureAwait(false))
            {
                switch (item)
                {
                    case IContentDelta delta:
                        lastSnapshot = message.AddContentDelta(delta);
                        break;

                    case TokenUsage tu:
                        tokenUsage = tu;
                        break;

                    case FinishReason fr:
                        finishReason = fr;
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException || ex is System.IO.IOException || ex is System.Net.Http.HttpRequestException)
        {
            caughtException = ex;
        }

        var consolidated = Consolidate(lastSnapshot);

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
