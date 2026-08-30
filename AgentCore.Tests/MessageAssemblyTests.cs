using System;
using System.Collections.Generic;
using System.Linq;
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
            new ToolCallContentStart(0, "ABC", "RunCommand"),
            new ToolCallContentDelta(0, "{\"commandLine\":\"ls\"}"),
            new ToolCallContentEnd(0),
            new MessageEnd()
        };

        var message = new Message(sequence.ToAsyncEnumerable());

        var contents = new List<IContent>();
        await foreach (var item in message.ContentsStream())
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
            new ToolCallContentStart(0, "A", "RunCommand"),
            new ToolCallContentStart(1, "B", "SearchWeb"),
            new ToolCallContentDelta(0, "{\"commandLine\":"),
            new ToolCallContentDelta(1, "{\"query\":"),
            new ToolCallContentDelta(0, "\"ls\"}"),
            new ToolCallContentDelta(1, "\"test\"}"),
            new ToolCallContentEnd(0),
            new ToolCallContentEnd(1),
            new MessageEnd()
        };

        var message = new Message(sequence.ToAsyncEnumerable());

        var contents = new List<IContent>();
        await foreach (var item in message.ContentsStream())
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
            new ReasoningContentStart(0),
            new ReasoningContentDelta(0, "Thinking deeply..."),
            new ReasoningContentEnd(0),
            new TextContentStart(1),
            new TextContentDelta(1, "Here is the answer."),
            new TextContentEnd(1),
            new MessageEnd(FinishReason: "stop", Usage: new TokenUsage(10, 20))
        };

        var message = new Message(sequence.ToAsyncEnumerable());

        var contents = new List<IContent>();
        await foreach (var item in message.ContentsStream())
        {
            contents.Add(item);
        }

        Assert.Equal(2, contents.Count);
        Assert.Equal("Thinking deeply...", Assert.IsType<Reasoning>(contents[0]).Thought);
        Assert.Equal("Here is the answer.", Assert.IsType<Text>(contents[1]).Value);

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
    }

    [Fact]
    public async Task Message_Receive_MultiBoundaryTransition_YieldsSettledContentsInOrder()
    {
        var sequence = new List<IMessageEvent>
        {
            new ReasoningContentStart(0),
            new ReasoningContentDelta(0, "Step 1: Analyze"),
            new ReasoningContentEnd(0),
            new ToolCallContentStart(1, "call_1", "lookup"),
            new ToolCallContentDelta(1, "{\"q\":"),
            new ToolCallContentDelta(1, "\"test\"}"),
            new ToolCallContentEnd(1),
            new TextContentStart(2),
            new TextContentDelta(2, "The result is ready."),
            new TextContentEnd(2),
            new MessageEnd()
        };

        var message = new Message(sequence.ToAsyncEnumerable());

        var contents = new List<IContent>();
        await foreach (var item in message.ContentsStream())
        {
            contents.Add(item);
        }

        Assert.Equal(3, contents.Count);
        Assert.IsType<Reasoning>(contents[0]);
        Assert.IsType<ToolCall>(contents[1]);
        Assert.IsType<Text>(contents[2]);

        Assert.Equal(3, message.Contents.Count);
    }

    [Fact]
    public async Task Message_ThreeParallelToolCalls_EmitsEarlyFinalizedCallFirst_AndPreservesIndexOrderInFinalContents()
    {
        var sequence = new List<IMessageEvent>
        {
            new ToolCallContentStart(0, "call_A", "ToolA"),
            new ToolCallContentDelta(0, "{\"a\":"),
            new ToolCallContentStart(1, "call_B", "ToolB"),
            new ToolCallContentDelta(1, "{\"b\":"),
            new ToolCallContentStart(2, "call_C", "ToolC"),
            new ToolCallContentDelta(2, "{\"c\":"),

            // ToolCall B (index 1) finishes FIRST
            new ToolCallContentDelta(1, "2}"),
            new ToolCallContentEnd(1),

            // ToolCall A and C continue
            new ToolCallContentDelta(0, "1}"),
            new ToolCallContentDelta(2, "3}"),

            // ToolCall C (index 2) finishes SECOND
            new ToolCallContentEnd(2),

            // ToolCall A (index 0) finishes LAST
            new ToolCallContentEnd(0),

            new MessageEnd()
        };

        var message = new Message(sequence.ToAsyncEnumerable());

        var yielded = new List<IContent>();
        await foreach (var item in message.ContentsStream())
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
    public async Task Message_Streaming_SingleConsumption_ThrowsOnSecondEnumeration()
    {
        var events = new IMessageEvent[]
        {
            new MessageStart(),
            new TextContentStart(0),
            new TextContentDelta(0, "hello"),
            new TextContentEnd(0),
            new MessageEnd()
        };

        var message = new Message(events.ToAsyncEnumerable());

        // First enumeration succeeds
        await foreach (var item in message.ContentsStream()) { }

        // Second enumeration throws
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var item in message.ContentsStream()) { }
        });
    }

    [Fact]
    public async Task Message_Streaming_MalformedOrIncompleteStreams_HandledGracefully()
    {
        // 1. Delta without explicit start creates accumulator on demand and yields content
        var bad1 = new IMessageEvent[] { new TextContentDelta(0, "graceful text"), new TextContentEnd(0) };
        var msg1 = new Message(bad1.ToAsyncEnumerable());
        var contents1 = new List<IContent>();
        await foreach (var c in msg1.ContentsStream()) contents1.Add(c);
        Assert.Equal("graceful text", Assert.Single(contents1.OfType<Text>()).Value);

        // 2. Stray end without start does not throw and does not yield empty block
        var bad2 = new IMessageEvent[] { new TextContentEnd(0) };
        var msg2 = new Message(bad2.ToAsyncEnumerable());
        var contents2 = new List<IContent>();
        await foreach (var c in msg2.ContentsStream()) contents2.Add(c);
        Assert.Empty(contents2);

        // 3. Duplicate start does not overwrite accumulated text
        var bad3 = new IMessageEvent[]
        {
            new TextContentStart(0),
            new TextContentDelta(0, "first"),
            new TextContentStart(0),
            new TextContentDelta(0, " second"),
            new TextContentEnd(0)
        };
        var msg3 = new Message(bad3.ToAsyncEnumerable());
        var contents3 = new List<IContent>();
        await foreach (var c in msg3.ContentsStream()) contents3.Add(c);
        Assert.Equal("first second", Assert.Single(contents3.OfType<Text>()).Value);

        // 4. Unclosed block on MessageEnd is gracefully completed and yielded
        var bad4 = new IMessageEvent[] { new TextContentStart(0), new TextContentDelta(0, "unclosed"), new MessageEnd() };
        var msg4 = new Message(bad4.ToAsyncEnumerable());
        var contents4 = new List<IContent>();
        await foreach (var c in msg4.ContentsStream()) contents4.Add(c);
        Assert.Equal("unclosed", Assert.Single(contents4.OfType<Text>()).Value);
        Assert.Equal("unclosed", Assert.Single(msg4.Contents.OfType<Text>()).Value);
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
            new Text("Initial message")
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
            new Text("Part 1"),
            new Reasoning("Part 2"),
            new ToolCall("call_1", "test", new System.Text.Json.Nodes.JsonObject())
        );

        Assert.Equal(3, message.Contents.Count);
        Assert.IsType<Text>(message.Contents[0]);
        Assert.IsType<Reasoning>(message.Contents[1]);
        Assert.IsType<ToolCall>(message.Contents[2]);
    }
}
