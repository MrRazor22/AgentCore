using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using Xunit;

namespace AgentCore.Tests;

public class MessageAssemblyTests
{
    private static MessageDelta D(IContentEvent content) => new(Content: content);

    [Fact]
    public void MessageAssembler_SequentialToolCalls_MergesCorrectly()
    {
        var assembler = new Assembler();
        assembler.Push(D(new ToolCallStart(0, "ABC", "RunCommand")));
        assembler.Push(D(new ToolCallDelta(0, "{\"commandLine\":\"ls\"}")));
        assembler.Push(D(new ToolCallEnd(0)));

        var message = assembler.ToMessage(new MessageEnd());
        var calls = message.Contents.OfType<ToolCall>().ToList();
        Assert.Single(calls);
        Assert.Equal("ABC", calls[0].Id);
        Assert.Equal("RunCommand", calls[0].Name);
        Assert.Contains("ls", calls[0].Arguments.ToString());
    }

    [Fact]
    public void MessageAssembler_MultipleSimultaneousInterleavedCalls_ResolvesCorrectly()
    {
        var assembler = new Assembler();
        assembler.Push(D(new ToolCallStart(0, "A", "RunCommand")));
        assembler.Push(D(new ToolCallStart(1, "B", "SearchWeb")));
        assembler.Push(D(new ToolCallDelta(0, "{\"commandLine\":")));
        assembler.Push(D(new ToolCallDelta(1, "{\"query\":")));
        assembler.Push(D(new ToolCallDelta(0, "\"ls\"}")));
        assembler.Push(D(new ToolCallDelta(1, "\"test\"}")));
        assembler.Push(D(new ToolCallEnd(0)));
        assembler.Push(D(new ToolCallEnd(1)));

        var message = assembler.ToMessage(new MessageEnd());
        var calls = message.Contents.OfType<ToolCall>().ToList();
        Assert.Equal(2, calls.Count);

        Assert.Equal("A", calls[0].Id);
        Assert.Equal("RunCommand", calls[0].Name);
        Assert.Contains("ls", calls[0].Arguments.ToString());

        Assert.Equal("B", calls[1].Id);
        Assert.Equal("SearchWeb", calls[1].Name);
        Assert.Contains("test", calls[1].Arguments.ToString());
    }

    [Fact]
    public void MessageAssembler_FluidAndStructuralStreaming_BehavesCorrectly()
    {
        var assembler = (Assembler)new Assembler().Create(new MessageStart(Role.Assistant, Id: "msg_123"));
        assembler.Push(new MessageDelta(Metadata: new ToolCallId("call_abc")));
        assembler.Push(D(new ReasoningStart(0)));
        assembler.Push(D(new ReasoningDelta(0, "Thinking deeply...")));
        assembler.Push(D(new ReasoningEnd(0)));
        assembler.Push(D(new TextStart(1)));
        assembler.Push(D(new TextDelta(1, "Here is the answer.")));
        assembler.Push(D(new TextEnd(1)));
        assembler.Push(new MessageDelta(Metadata: new TokenUsage(10, 20, 30)));

        var message = assembler.ToMessage(new MessageEnd());
        Assert.Equal(2, message.Contents.Count);
        Assert.Equal(Role.Assistant, message.Role);
        Assert.Equal("Thinking deeply...", Assert.IsType<Reasoning>(message.Contents[0]).Thought);
        Assert.Equal("Here is the answer.", Assert.IsType<Text>(message.Contents[1]).Value);

        Assert.Equal("msg_123", message.Id);
        Assert.Equal("call_abc", message.Get<ToolCallId>()?.Value);
        var usage = message.Get<TokenUsage>();
        Assert.NotNull(usage);
        Assert.Equal(10, usage.InputTokens);
        Assert.Equal(20, usage.OutputTokens);
        Assert.Equal(30, usage.TotalTokens);
    }

    [Fact]
    public void MessageAssembler_PreservesInterleavedOrder()
    {
        var assembler = new Assembler();
        assembler.Push(D(new TextStart(0)));
        assembler.Push(D(new TextDelta(0, "First text. ")));
        assembler.Push(D(new ToolCallStart(1, "call_1", "Search")));
        assembler.Push(D(new ToolCallDelta(1, "{\"q\": \"c#\"}")));
        assembler.Push(D(new TextStart(2)));
        assembler.Push(D(new TextDelta(2, "Second text.")));

        var message = assembler.ToMessage(new MessageEnd());
        Assert.Equal(3, message.Contents.Count);
        Assert.Equal("First text. ", Assert.IsType<Text>(message.Contents[0]).Value);
        Assert.Equal("call_1", Assert.IsType<ToolCall>(message.Contents[1]).Id);
        Assert.Equal("Second text.", Assert.IsType<Text>(message.Contents[2]).Value);
    }
}
