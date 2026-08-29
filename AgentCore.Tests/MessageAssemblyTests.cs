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
    public void MessageAccumulator_SequentialToolCalls_MergesCorrectly()
    {
        var accumulator = new MessageAccumulator();
        var sequence = new List<IMessageEvent>
        {
            new ToolCallContentStart("ABC", "RunCommand", Index: 0),
            new ToolCallContentDelta("ABC", "{\"commandLine\":\"ls\"}", Index: 0),
            new ToolCallContentEnd("ABC", Index: 0)
        };

        var contents = sequence.SelectMany(accumulator.Receive).ToList();
        Assert.NotNull(contents);
        var calls = contents.OfType<ToolCall>().ToList();
        Assert.Single(calls);
        Assert.Equal("ABC", calls[0].Id);
        Assert.Equal("RunCommand", calls[0].Name);
        Assert.Contains("ls", calls[0].Arguments.ToString());
    }

    [Fact]
    public void MessageAccumulator_MultipleSimultaneousInterleavedCalls_ResolvesCorrectly()
    {
        var accumulator = new MessageAccumulator();
        var sequence = new List<IMessageEvent>
        {
            new ToolCallContentStart("A", "RunCommand", Index: 0),
            new ToolCallContentStart("B", "SearchWeb", Index: 1),
            new ToolCallContentDelta("A", "{\"commandLine\":", Index: 0),
            new ToolCallContentDelta("B", "{\"query\":", Index: 1),
            new ToolCallContentDelta("A", "\"ls\"}", Index: 0),
            new ToolCallContentDelta("B", "\"test\"}", Index: 1),
            new ToolCallContentEnd("A", Index: 0),
            new ToolCallContentEnd("B", Index: 1)
        };

        var contents = sequence.SelectMany(accumulator.Receive).ToList();
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
    public void MessageAccumulator_Receive_FluidAndStructuralStreaming_BehavesCorrectly()
    {
        var accumulator = new MessageAccumulator();

        accumulator.Receive(new MessageStart(Role.Assistant, Id: "msg_123", Model: "gpt-4o"));
        accumulator.Receive(new ReasoningContentDelta("Thinking deeply..."));
        var r1 = accumulator.Receive(new ReasoningContentEnd()).ToList();
        Assert.Single(r1);
        Assert.Equal("Thinking deeply...", Assert.IsType<Reasoning>(r1[0]).Thought);

        accumulator.Receive(new TextContentDelta("Here is the answer."));
        var r2 = accumulator.Receive(new TextContentEnd()).ToList();
        Assert.Single(r2);
        Assert.Equal("Here is the answer.", Assert.IsType<Text>(r2[0]).Value);

        accumulator.Receive(new MessageEnd(FinishReason: "stop", Usage: new TokenUsage(10, 20)));
        var message = accumulator.ToMessage();

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
    public void MessageAccumulator_Receive_MultiBoundaryTransition_YieldsSettledContentsInOrder()
    {
        var accumulator = new MessageAccumulator();

        // Reasoning
        accumulator.Receive(new ReasoningContentDelta("Step 1: Analyze"));
        var r1 = accumulator.Receive(new ReasoningContentEnd()).ToList();
        Assert.Single(r1);
        Assert.Equal("Step 1: Analyze", Assert.IsType<Reasoning>(r1[0]).Thought);

        // ToolCall
        accumulator.Receive(new ToolCallContentStart("call_1", "lookup"));
        accumulator.Receive(new ToolCallContentDelta("call_1", "{\"q\":"));
        accumulator.Receive(new ToolCallContentDelta("call_1", "\"test\"}"));
        var r2 = accumulator.Receive(new ToolCallContentEnd("call_1")).ToList();
        Assert.Single(r2);
        var tc = Assert.IsType<ToolCall>(r2[0]);
        Assert.Equal("call_1", tc.Id);
        Assert.Equal("lookup", tc.Name);

        // Text
        accumulator.Receive(new TextContentDelta("The result is ready."));
        var r3 = accumulator.Receive(new TextContentEnd()).ToList();
        Assert.Single(r3);
        Assert.Equal("The result is ready.", Assert.IsType<Text>(r3[0]).Value);

        var message = accumulator.ToMessage();
        Assert.Equal(3, message.Contents.Count);
        Assert.IsType<Reasoning>(message.Contents[0]);
        Assert.IsType<ToolCall>(message.Contents[1]);
        Assert.IsType<Text>(message.Contents[2]);
    }

    [Fact]
    public void MessageAccumulator_ThreeParallelToolCalls_EmitsEarlyFinalizedCallFirst()
    {
        var accumulator = new MessageAccumulator();

        // Start 3 parallel tool calls
        accumulator.Receive(new ToolCallContentStart("call_A", "ToolA", Index: 0)).ToList();
        accumulator.Receive(new ToolCallContentDelta("call_A", "{\"a\":", Index: 0)).ToList();

        accumulator.Receive(new ToolCallContentStart("call_B", "ToolB", Index: 1)).ToList();
        accumulator.Receive(new ToolCallContentDelta("call_B", "{\"b\":", Index: 1)).ToList();

        accumulator.Receive(new ToolCallContentStart("call_C", "ToolC", Index: 2)).ToList();
        accumulator.Receive(new ToolCallContentDelta("call_C", "{\"c\":", Index: 2)).ToList();

        // ToolCall B finishes FIRST
        accumulator.Receive(new ToolCallContentDelta("call_B", "2}", Index: 1)).ToList();
        var bFinish = accumulator.Receive(new ToolCallContentEnd("call_B", Index: 1)).ToList();
        Assert.Single(bFinish);
        var tcB = Assert.IsType<ToolCall>(bFinish[0]);
        Assert.Equal("call_B", tcB.Id);
        Assert.Equal("ToolB", tcB.Name);

        // ToolCall A and C continue accumulation
        accumulator.Receive(new ToolCallContentDelta("call_A", "1}", Index: 0)).ToList();
        accumulator.Receive(new ToolCallContentDelta("call_C", "3}", Index: 2)).ToList();

        // ToolCall C finishes SECOND
        var cFinish = accumulator.Receive(new ToolCallContentEnd("call_C", Index: 2)).ToList();
        Assert.Single(cFinish);
        Assert.Equal("call_C", Assert.IsType<ToolCall>(cFinish[0]).Id);

        // ToolCall A finishes LAST
        var aFinish = accumulator.Receive(new ToolCallContentEnd("call_A", Index: 0)).ToList();
        Assert.Single(aFinish);
        Assert.Equal("call_A", Assert.IsType<ToolCall>(aFinish[0]).Id);
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
