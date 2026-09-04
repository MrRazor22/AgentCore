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
            new ToolCallStart(0, "ABC", "RunCommand"),
            new ToolCallDelta(0, "{\"commandLine\":\"ls\"}"),
            new ToolCallEnd(0),
            new MessageEnd()
        };

        var message = new StreamingMessage(Role.Assistant);

        var contents = new List<IContent>();
        await foreach (var item in message.Receive(sequence.ToAsyncEnumerable()))
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

        var message = new StreamingMessage(Role.Assistant);

        var contents = new List<IContent>();
        await foreach (var item in message.Receive(sequence.ToAsyncEnumerable()))
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

        var message = new StreamingMessage(Role.Assistant);

        var contents = new List<IContent>();
        await foreach (var item in message.Receive(sequence.ToAsyncEnumerable()))
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
            new ReasoningStart(0),
            new ReasoningDelta(0, "Step 1: Analyze"),
            new ReasoningEnd(0),
            new ToolCallStart(1, "call_1", "lookup"),
            new ToolCallDelta(1, "{\"q\":"),
            new ToolCallDelta(1, "\"test\"}"),
            new ToolCallEnd(1),
            new TextStart(2),
            new TextDelta(2, "The result is ready."),
            new TextEnd(2),
            new MessageEnd()
        };

        var message = new StreamingMessage(Role.Assistant);

        var contents = new List<IContent>();
        await foreach (var item in message.Receive(sequence.ToAsyncEnumerable()))
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

        var message = new StreamingMessage(Role.Assistant);

        var yielded = new List<IContent>();
        await foreach (var item in message.Receive(sequence.ToAsyncEnumerable()))
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
        var msg1 = new StreamingMessage(Role.Assistant);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in msg1.Receive(bad1.ToAsyncEnumerable())) { }
        });

        // 2. Stray end without start throws protocol violation
        var bad2 = new IMessageEvent[] { new TextEnd(0) };
        var msg2 = new StreamingMessage(Role.Assistant);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in msg2.Receive(bad2.ToAsyncEnumerable())) { }
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
        var msg3 = new StreamingMessage(Role.Assistant);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in msg3.Receive(bad3.ToAsyncEnumerable())) { }
        });

        // 4. Unclosed block on MessageEnd is gracefully completed and yielded
        var bad4 = new IMessageEvent[] { new TextStart(0), new TextDelta(0, "unclosed"), new MessageEnd() };
        var msg4 = new StreamingMessage(Role.Assistant);
        var contents4 = new List<IContent>();
        await foreach (var c in msg4.Receive(bad4.ToAsyncEnumerable())) contents4.Add(c);
        Assert.Equal("unclosed", Assert.Single(contents4.OfType<Text>()).Value);
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
    public void StreamingContent_StandaloneClasses_AccumulateAndProduceContent()
    {
        // StreamingText
        var stText = new StreamingText();
        stText.Receive(new TextDelta(0, "Hello, "));
        stText.Receive(new TextDelta(0, "world!"));
        var textContent = stText.ToContent();
        Assert.Equal("Hello, world!", textContent.Value);

        // StreamingReasoning
        var stReasoning = new StreamingReasoning();
        stReasoning.Receive(new ReasoningDelta(0, "Plan step 1. "));
        stReasoning.Receive(new ReasoningDelta(0, "Plan step 2."));
        var reasoningContent = stReasoning.ToContent();
        Assert.Equal("Plan step 1. Plan step 2.", reasoningContent.Thought);

        // StreamingToolCall
        var stTool = new StreamingToolCall("call_1", "calc");
        stTool.Receive(new ToolCallDelta(0, "{\"expr\":"));
        stTool.Receive(new ToolCallDelta(0, "\"1 + 1\"}"));
        var toolContent = stTool.ToContent();
        Assert.Equal("call_1", toolContent.Id);
        Assert.Equal("calc", toolContent.Name);
        Assert.Equal("1 + 1", toolContent.Arguments["expr"]?.ToString());
    }

    [Fact]
    public async Task StreamingContent_ChannelBackedDeltas_YieldsInRealTime_AndCompletes()
    {
        // 1. StreamingText
        var stText = new StreamingText();
        stText.Receive(new TextDelta(0, "Hello, "));
        stText.Receive(new TextDelta(0, "world!"));
        stText.Complete();

        var textDeltas = new List<string>();
        await foreach (var delta in stText)
        {
            textDeltas.Add(delta.Text);
        }
        Assert.Equal(["Hello, ", "world!"], textDeltas);
        Assert.Equal("Hello, world!", stText.ToContent().Value);
        Assert.True(stText.EstimateTokens() > 0);
        Assert.Equal("Hello, world!", stText.ToString());

        // 2. StreamingReasoning
        var stReasoning = new StreamingReasoning();
        stReasoning.Receive(new ReasoningDelta(0, "Think 1. "));
        stReasoning.Receive(new ReasoningDelta(0, "Think 2."));
        stReasoning.Complete();

        var reasoningDeltas = new List<string>();
        await foreach (var delta in stReasoning)
        {
            reasoningDeltas.Add(delta.Thought);
        }
        Assert.Equal(["Think 1. ", "Think 2."], reasoningDeltas);
        Assert.Equal("Think 1. Think 2.", stReasoning.ToContent().Thought);

        // 3. StreamingToolCall
        var stTool = new StreamingToolCall("call_99", "fn");
        stTool.Receive(new ToolCallDelta(0, "{\"x\":"));
        stTool.Receive(new ToolCallDelta(0, "42}"));
        stTool.Complete();

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
    public void StreamingContent_Truncate_ReturnsMaterializedContent()
    {
        var stText = new StreamingText();
        stText.Receive(new TextDelta(0, "This is a very long text that will be truncated."));
        
        var truncated = stText.Truncate(3, "...");
        Assert.IsType<Text>(truncated);
        Assert.Contains("...", ((Text)truncated).Value);
    }
}
