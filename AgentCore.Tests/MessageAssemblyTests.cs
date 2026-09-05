using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
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
    public async Task Message_SequentialToolCalls_MergesAndStreamsCorrectly()
    {
        var sequence = new List<IMessageEvent>
        {
            new ToolCallStart(0, "ABC", "RunCommand"),
            new ToolCallDelta(0, "{\"commandLine\":\"ls\"}"),
            new ToolCallEnd(0),
            new MessageEnd()
        };

        var message = new StreamingMessage(sequence.ToAsyncEnumerable(), Role.Assistant);

        var contents = new List<IContent>();
        await foreach (var item in message)
        {
            contents.Add(item);
        }

        var calls = contents.OfType<ToolCall>().ToList();
        Assert.Single(calls);
        Assert.Equal("ABC", calls[0].Id);
        Assert.Equal("RunCommand", calls[0].Name);
        Assert.Contains("ls", calls[0].Arguments.ToString());

        Assert.Single(message.Contents);
        Assert.Equal("ABC", ((ToolCall)message.Contents[0]).Id);
    }

    [Fact]
    public async Task Message_MultipleSimultaneousInterleavedCalls_ResolvesCorrectly()
    {
        var sequence = new List<IMessageEvent>
        {
            new ToolCallStart(0, "A", "RunCommand"),
            new ToolCallStart(1, "B", "SearchWeb"),
            new ToolCallDelta(0, "{\"commandLine\":"),
            new ToolCallDelta(1, "{\"query\":"),
            new ToolCallDelta(0, "\"ls\"}"),
            new ToolCallDelta(1, "\"test\"}"),
            new ToolCallEnd(0),
            new ToolCallEnd(1),
            new MessageEnd()
        };

        var message = new StreamingMessage(sequence.ToAsyncEnumerable(), Role.Assistant);

        var contents = new List<IContent>();
        await foreach (var item in message)
        {
            contents.Add(item);
        }

        var calls = contents.OfType<ToolCall>().ToList();
        Assert.Equal(2, calls.Count);

        Assert.Equal("A", calls[0].Id);
        Assert.Equal("RunCommand", calls[0].Name);
        Assert.Contains("ls", calls[0].Arguments.ToString());

        Assert.Equal("B", calls[1].Id);
        Assert.Equal("SearchWeb", calls[1].Name);
        Assert.Contains("test", calls[1].Arguments.ToString());

        Assert.Equal(2, message.Contents.Count);
    }

    [Fact]
    public async Task Message_Receive_FluidAndStructuralStreaming_BehavesCorrectly()
    {
        var sequence = new List<IMessageEvent>
        {
            new MessageStart(Role.Assistant, Id: "msg_123", Model: "gpt-4o"),
            new ReasoningStart(0),
            new ReasoningDelta(0, "Thinking deeply..."),
            new ReasoningEnd(0),
            new TextStart(1),
            new TextDelta(1, "Here is the answer."),
            new TextEnd(1),
            new MessageEnd(FinishReason: "stop", Usage: new TokenUsage(10, 20))
        };

        var message = new StreamingMessage(sequence.ToAsyncEnumerable(), Role.Assistant);

        var contents = new List<IContent>();
        await foreach (var item in message)
        {
            contents.Add(item);
        }

        Assert.Equal(2, contents.Count);
        Assert.Equal("Thinking deeply...", (contents[0] is IStreamingContent sc0 ? sc0.ToContent() : contents[0]) is Reasoning r ? r.Thought : "");
        Assert.Equal("Here is the answer.", (contents[1] is IStreamingContent sc1 ? sc1.ToContent() : contents[1]) is Text t ? t.Value : "");

        Assert.Equal(2, message.Contents.Count);
        Assert.Equal(Role.Assistant, message.Role);
        Assert.Equal("Thinking deeply...", Assert.IsType<Reasoning>(message.Contents[0]).Thought);
        Assert.Equal("Here is the answer.", Assert.IsType<Text>(message.Contents[1]).Value);
        Assert.NotNull(message.Metadata);
        Assert.Equal("msg_123", message.Metadata.Id);
        Assert.Equal("gpt-4o", message.Metadata.Model);
        Assert.Equal("stop", message.Metadata.FinishReason);
        Assert.Equal(10, message.Metadata.Usage?.InputTokens);
        Assert.Equal(20, message.Metadata.Usage?.OutputTokens);
        Assert.Equal(30, message.Metadata.Usage?.TotalTokens);
    }

    [Fact]
    public async Task Message_AllBlocksInterleaved_CorrectlySeparatesAndPreservesTypes()
    {
        var sequence = new List<IMessageEvent>
        {
            new ReasoningStart(0),
            new ToolCallStart(1, "call_42", "Search"),
            new ReasoningDelta(0, "I need to search first. "),
            new ToolCallDelta(1, "{\"query\":"),
            new ReasoningDelta(0, "Then analyze results."),
            new ToolCallDelta(1, "\"dotnet 9\"}"),
            new ReasoningEnd(0),
            new ToolCallEnd(1),
            new TextStart(2),
            new TextDelta(2, "The result is ready."),
            new TextEnd(2),
            new MessageEnd()
        };

        var message = new StreamingMessage(sequence.ToAsyncEnumerable(), Role.Assistant);

        var contents = new List<IContent>();
        await foreach (var item in message)
        {
            contents.Add(item);
        }

        Assert.Equal(3, contents.Count);
        Assert.True(contents[0] is StreamingReasoning or Reasoning);
        Assert.True(contents[1] is StreamingToolCall or ToolCall);
        Assert.True(contents[2] is StreamingText or Text);

        Assert.Equal(3, message.Contents.Count);
    }

    [Fact]
    public async Task Message_ThreeParallelToolCalls_EmitsEarlyFinalizedCallFirst_AndPreservesIndexOrderInFinalContents()
    {
        var sequence = new List<IMessageEvent>
        {
            new ToolCallStart(0, "call_A", "ToolA"),
            new ToolCallDelta(0, "{\"a\":"),
            new ToolCallStart(1, "call_B", "ToolB"),
            new ToolCallDelta(1, "{\"b\":"),
            new ToolCallStart(2, "call_C", "ToolC"),
            new ToolCallDelta(2, "{\"c\":"),

            // ToolCall B (index 1) finishes FIRST
            new ToolCallDelta(1, "2}"),
            new ToolCallEnd(1),

            // ToolCall A and C continue
            new ToolCallDelta(0, "1}"),
            new ToolCallDelta(2, "3}"),

            // ToolCall C (index 2) finishes SECOND
            new ToolCallEnd(2),

            // ToolCall A (index 0) finishes LAST
            new ToolCallEnd(0),

            new MessageEnd()
        };

        var message = new StreamingMessage(sequence.ToAsyncEnumerable(), Role.Assistant);

        var yielded = new List<IContent>();
        await foreach (var item in message)
        {
            yielded.Add(item);
        }

        // Live stream order was B -> C -> A
        Assert.Equal(3, yielded.Count);
        Assert.Equal("call_B", ((ToolCall)yielded[0]).Id);
        Assert.Equal("call_C", ((ToolCall)yielded[1]).Id);
        Assert.Equal("call_A", ((ToolCall)yielded[2]).Id);

        // Final message contents must be sorted by logical index (0, 1, 2) -> [call_A, call_B, call_C]
        Assert.Equal(3, message.Contents.Count);
        Assert.Equal("call_A", ((ToolCall)message.Contents[0]).Id);
        Assert.Equal("call_B", ((ToolCall)message.Contents[1]).Id);
        Assert.Equal("call_C", ((ToolCall)message.Contents[2]).Id);
    }

    [Fact]
    public async Task Message_Streaming_MalformedOrIncompleteStreams_HandledGracefully()
    {
        // 1. Delta without explicit start throws protocol violation
        var bad1 = new IMessageEvent[] { new TextDelta(0, "graceful text"), new TextEnd(0) };
        var msg1 = new StreamingMessage(bad1.ToAsyncEnumerable(), Role.Assistant);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in msg1) { }
        });

        // 2. Stray end without start throws protocol violation
        var bad2 = new IMessageEvent[] { new TextEnd(0) };
        var msg2 = new StreamingMessage(bad2.ToAsyncEnumerable(), Role.Assistant);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in msg2) { }
        });

        // 3. Duplicate start throws protocol violation
        var bad3 = new IMessageEvent[]
        {
            new TextStart(0),
            new TextDelta(0, "first"),
            new TextStart(0),
            new TextDelta(0, " second"),
            new TextEnd(0)
        };
        var msg3 = new StreamingMessage(bad3.ToAsyncEnumerable(), Role.Assistant);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in msg3) { }
        });

        // 4. Unclosed block on MessageEnd is gracefully completed and yielded
        var bad4 = new IMessageEvent[] { new TextStart(0), new TextDelta(0, "unclosed"), new MessageEnd() };
        var msg4 = new StreamingMessage(bad4.ToAsyncEnumerable(), Role.Assistant);
        var contents4 = new List<IContent>();
        await foreach (var c in msg4) contents4.Add(c);
        Assert.Equal("unclosed", Assert.Single(contents4.Select(c => c is IStreamingContent sc ? sc.ToContent() : c).OfType<Text>()).Value);
        Assert.Equal("unclosed", Assert.Single(msg4.Contents.OfType<Text>()).Value);
    }

    [Fact]
    public void Message_Serialization_SerializesCommittedContents()
    {
        var message = new Message(Role.Assistant, [new Text("Committed message")]);

        var json = System.Text.Json.JsonSerializer.Serialize(message);
        Assert.Contains("\"role\":\"Assistant\"", json);
        Assert.Contains("Committed message", json);
    }

    [Fact]
    public void Message_Constructor_InitializesContentsCorrectly()
    {
        var message = new Message(
            Role.Assistant,
            [new Text("Initial message")]
        );

        Assert.Equal(Role.Assistant, message.Role);
        Assert.Single(message.Contents);
        Assert.Equal("Initial message", ((Text)message.Contents[0]).Value);
    }

    [Fact]
    public void Message_MultipleContents_PreservesOrder()
    {
        var message = new Message(
            Role.Assistant,
            [
                new Text("Part 1"),
                new Reasoning("Part 2"),
                new ToolCall("call_1", "test", new System.Text.Json.Nodes.JsonObject())
            ]
        );

        Assert.Equal(3, message.Contents.Count);
        Assert.IsType<Text>(message.Contents[0]);
        Assert.IsType<Reasoning>(message.Contents[1]);
        Assert.IsType<ToolCall>(message.Contents[2]);
    }

    [Fact]
    public async Task StreamingContent_StandaloneClasses_AccumulateAndProduceContent()
    {
        // StreamingText
        var stText = new StreamingText(ToAsyncStream([new TextDelta(0, "Hello, "), new TextDelta(0, "world!")]));
        await foreach (var _ in stText) { }
        var textContent = stText.ToContent();
        Assert.Equal("Hello, world!", textContent.Value);

        // StreamingReasoning
        var stReasoning = new StreamingReasoning(ToAsyncStream([new ReasoningDelta(0, "Plan step 1. "), new ReasoningDelta(0, "Plan step 2.")]));
        await foreach (var _ in stReasoning) { }
        var reasoningContent = stReasoning.ToContent();
        Assert.Equal("Plan step 1. Plan step 2.", reasoningContent.Thought);

        // StreamingToolCall
        var stTool = new StreamingToolCall("call_1", "calc", ToAsyncStream([new ToolCallDelta(0, "{\"expr\":"), new ToolCallDelta(0, "\"1 + 1\"}")]));
        await foreach (var _ in stTool) { }
        var toolContent = stTool.ToContent();
        Assert.Equal("call_1", toolContent.Id);
        Assert.Equal("calc", toolContent.Name);
        Assert.Equal("1 + 1", toolContent.Arguments["expr"]?.ToString());
    }

    [Fact]
    public async Task StreamingContent_ChannelBackedDeltas_YieldsInRealTime_AndCompletes()
    {
        // 1. StreamingText
        var stText = new StreamingText(ToAsyncStream([new TextDelta(0, "Hello, "), new TextDelta(0, "world!")]));
        var textDeltas = new List<string>();
        await foreach (var delta in stText)
        {
            textDeltas.Add(delta.Text);
        }
        Assert.Equal(["Hello, ", "world!"], textDeltas);
        Assert.Equal("Hello, world!", stText.ToContent().Value);
        Assert.Equal("Hello, world!", stText.ToString());

        // 2. StreamingReasoning
        var stReasoning = new StreamingReasoning(ToAsyncStream([new ReasoningDelta(0, "Think 1. "), new ReasoningDelta(0, "Think 2.")]));
        var reasoningDeltas = new List<string>();
        await foreach (var delta in stReasoning)
        {
            reasoningDeltas.Add(delta.Thought);
        }
        Assert.Equal(["Think 1. ", "Think 2."], reasoningDeltas);
        Assert.Equal("Think 1. Think 2.", stReasoning.ToContent().Thought);

        // 3. StreamingToolCall
        var stTool = new StreamingToolCall("call_99", "fn", ToAsyncStream([new ToolCallDelta(0, "{\"x\":"), new ToolCallDelta(0, "42}")]));
        var toolDeltas = new List<string>();
        await foreach (var delta in stTool)
        {
            toolDeltas.Add(delta.Arguments);
        }
        Assert.Equal(["{\"x\":", "42}"], toolDeltas);
        var toolCall = stTool.ToContent();
        Assert.Equal("call_99", toolCall.Id);
        Assert.Equal("fn", toolCall.Name);
        Assert.Equal("42", toolCall.Arguments["x"]?.ToString());
    }

    [Fact]
    public async Task StreamingContent_Truncate_ReturnsMaterializedContent()
    {
        var stText = new StreamingText(ToAsyncStream([new TextDelta(0, "This is a very long text that will be truncated.")]));
        await foreach (var _ in stText) { }
        var truncated = stText.ToContent().Truncate(3, "...");
        Assert.IsType<Text>(truncated);
        Assert.Contains("...", ((Text)truncated).Value);
    }

    [Fact]
    public async Task StreamingContent_CompleteWithError_PropagatesExceptionToEnumerator()
    {
        static async IAsyncEnumerable<TextDelta> FailingTextStream()
        {
            yield return new TextDelta(0, "Part 1. ");
            await Task.Yield();
            throw new InvalidOperationException("Provider exploded");
        }

        var stText = new StreamingText(FailingTextStream());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var delta in stText)
            {
                // read
            }
        });

        Assert.Equal("Provider exploded", ex.Message);
    }

    [Fact]
    public async Task StreamingMessage_ProviderException_CompletesActiveStreamsWithError()
    {
        static async IAsyncEnumerable<IMessageEvent> FailingStream()
        {
            yield return new TextStart(0);
            yield return new TextDelta(0, "First chunk");
            await Task.Yield();
            throw new HttpRequestException("Connection terminated abruptly");
        }

        var msg = new StreamingMessage(FailingStream(), Role.Assistant);

        // When the provider throws, StreamingMessage rethrows on enumeration
        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var content in msg)
            {
                // Never reaches yield because TextEnd was never emitted
            }
        });
        Assert.Equal("Connection terminated abruptly", ex.Message);
    }
}
