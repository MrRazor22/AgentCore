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
    public void ContentAssembler_SequentialToolCalls_MergesCorrectly()
    {
        var assembler = new ContentAssembler();
        var sequence = new List<ILLMOutput>
        {
            new ToolCallStart("ABC", "RunCommand", Index: 0),
            new ToolCallDelta("ABC", "{\"commandLine\":\"ls\"}", Index: 0),
            new ToolCallEnd("ABC", Index: 0)
        };

        var contents = sequence.SelectMany(assembler.Receive).ToList();
        Assert.NotNull(contents);
        var calls = contents.OfType<ToolCall>().ToList();
        Assert.Single(calls);
        Assert.Equal("ABC", calls[0].Id);
        Assert.Equal("RunCommand", calls[0].Name);
        Assert.Contains("ls", calls[0].Arguments.ToString());
    }

    [Fact]
    public void ContentAssembler_MultipleSimultaneousInterleavedCalls_ResolvesCorrectly()
    {
        var assembler = new ContentAssembler();
        var sequence = new List<ILLMOutput>
        {
            new ToolCallStart("A", "RunCommand", Index: 0),
            new ToolCallStart("B", "SearchWeb", Index: 1),
            new ToolCallDelta("A", "{\"commandLine\":", Index: 0),
            new ToolCallDelta("B", "{\"query\":", Index: 1),
            new ToolCallDelta("A", "\"ls\"}", Index: 0),
            new ToolCallDelta("B", "\"test\"}", Index: 1),
            new ToolCallEnd("A", Index: 0),
            new ToolCallEnd("B", Index: 1)
        };

        var contents = sequence.SelectMany(assembler.Receive).ToList();
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
    public void Message_Receive_FluidAndStructuralStreaming_BehavesCorrectly()
    {
        var message = new Message(Role.Assistant);

        message.Receive(new ReasoningDelta("Thinking deeply..."));
        var r1 = message.Receive(new ReasoningEnd()).ToList();
        Assert.Single(r1);
        Assert.Equal("Thinking deeply...", Assert.IsType<Reasoning>(r1[0]).Thought);

        message.Receive(new TextDelta("Here is the answer."));
        var r2 = message.Receive(new TextEnd()).ToList();
        Assert.Single(r2);
        Assert.Equal("Here is the answer.", Assert.IsType<Text>(r2[0]).Value);

        Assert.Equal(2, message.Contents.Count);
        Assert.Equal("Thinking deeply...", Assert.IsType<Reasoning>(message.Contents[0]).Thought);
        Assert.Equal("Here is the answer.", Assert.IsType<Text>(message.Contents[1]).Value);
    }

    [Fact]
    public void Message_Receive_MultiBoundaryTransition_YieldsSettledContentsInOrder()
    {
        var message = new Message(Role.Assistant);

        // Reasoning
        message.Receive(new ReasoningDelta("Step 1: Analyze"));
        var r1 = message.Receive(new ReasoningEnd()).ToList();
        Assert.Single(r1);
        Assert.Equal("Step 1: Analyze", Assert.IsType<Reasoning>(r1[0]).Thought);

        // ToolCall
        message.Receive(new ToolCallStart("call_1", "lookup"));
        message.Receive(new ToolCallDelta("call_1", "{\"q\":"));
        message.Receive(new ToolCallDelta("call_1", "\"test\"}"));
        var r2 = message.Receive(new ToolCallEnd("call_1")).ToList();
        Assert.Single(r2);
        var tc = Assert.IsType<ToolCall>(r2[0]);
        Assert.Equal("call_1", tc.Id);
        Assert.Equal("lookup", tc.Name);

        // Text
        message.Receive(new TextDelta("The result is ready."));
        var r3 = message.Receive(new TextEnd()).ToList();
        Assert.Single(r3);
        Assert.Equal("The result is ready.", Assert.IsType<Text>(r3[0]).Value);

        Assert.Equal(3, message.Contents.Count);
        Assert.IsType<Reasoning>(message.Contents[0]);
        Assert.IsType<ToolCall>(message.Contents[1]);
        Assert.IsType<Text>(message.Contents[2]);
    }

    [Fact]
    public void ContentAssembler_ThreeParallelToolCalls_EmitsEarlyFinalizedCallFirst()
    {
        var assembler = new ContentAssembler();

        // Start 3 parallel tool calls
        assembler.Receive(new ToolCallStart("call_A", "ToolA", Index: 0)).ToList();
        assembler.Receive(new ToolCallDelta("call_A", "{\"a\":", Index: 0)).ToList();

        assembler.Receive(new ToolCallStart("call_B", "ToolB", Index: 1)).ToList();
        assembler.Receive(new ToolCallDelta("call_B", "{\"b\":", Index: 1)).ToList();

        assembler.Receive(new ToolCallStart("call_C", "ToolC", Index: 2)).ToList();
        assembler.Receive(new ToolCallDelta("call_C", "{\"c\":", Index: 2)).ToList();

        // ToolCall B finishes FIRST
        assembler.Receive(new ToolCallDelta("call_B", "2}", Index: 1)).ToList();
        var bFinish = assembler.Receive(new ToolCallEnd("call_B", Index: 1)).ToList();
        Assert.Single(bFinish);
        var tcB = Assert.IsType<ToolCall>(bFinish[0]);
        Assert.Equal("call_B", tcB.Id);
        Assert.Equal("ToolB", tcB.Name);

        // ToolCall A and C continue accumulation
        assembler.Receive(new ToolCallDelta("call_A", "1}", Index: 0)).ToList();
        assembler.Receive(new ToolCallDelta("call_C", "3}", Index: 2)).ToList();

        // ToolCall C finishes SECOND
        var cFinish = assembler.Receive(new ToolCallEnd("call_C", Index: 2)).ToList();
        Assert.Single(cFinish);
        Assert.Equal("call_C", Assert.IsType<ToolCall>(cFinish[0]).Id);

        // ToolCall A finishes LAST
        var aFinish = assembler.Receive(new ToolCallEnd("call_A", Index: 0)).ToList();
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
