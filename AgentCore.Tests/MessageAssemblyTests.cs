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
    [Fact]
    public void StreamingMessage_SequentialToolCalls_MergesCorrectly()
    {
        var message = new StreamingMessage(Role.Assistant);
        message.Push(new ToolCallStart(0, "ABC", "RunCommand"));
        message.Push(new ToolCallDelta(0, "{\"commandLine\":\"ls\"}"));
        message.Push(new ToolCallEnd(0));
        message.Push(new MessageEnd());

        var calls = message.Contents.OfType<ToolCall>().ToList();
        Assert.Single(calls);
        Assert.Equal("ABC", calls[0].Id);
        Assert.Equal("RunCommand", calls[0].Name);
        Assert.Contains("ls", calls[0].Arguments.ToString());
    }

    [Fact]
    public void StreamingMessage_MultipleSimultaneousInterleavedCalls_ResolvesCorrectly()
    {
        var message = new StreamingMessage(Role.Assistant);
        message.Push(new ToolCallStart(0, "A", "RunCommand"));
        message.Push(new ToolCallStart(1, "B", "SearchWeb"));
        message.Push(new ToolCallDelta(0, "{\"commandLine\":"));
        message.Push(new ToolCallDelta(1, "{\"query\":"));
        message.Push(new ToolCallDelta(0, "\"ls\"}"));
        message.Push(new ToolCallDelta(1, "\"test\"}"));
        message.Push(new ToolCallEnd(0));
        message.Push(new ToolCallEnd(1));
        message.Push(new MessageEnd());

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
    public void StreamingMessage_FluidAndStructuralStreaming_BehavesCorrectly()
    {
        var message = new StreamingMessage();
        message.Push(new MessageStart(Role.Assistant, Id: "msg_123", Model: "gpt-4o"));
        message.Push(new ReasoningStart(0));
        message.Push(new ReasoningDelta(0, "Thinking deeply..."));
        message.Push(new ReasoningEnd(0));
        message.Push(new TextStart(1));
        message.Push(new TextDelta(1, "Here is the answer."));
        message.Push(new TextEnd(1));
        message.Push(new MessageEnd(FinishReason: "stop", Usage: new TokenUsage(10, 20)));

        Assert.Equal(2, message.Contents.Count);
        Assert.Equal(Role.Assistant, message.Role);
        Assert.Equal("Thinking deeply...", Assert.IsType<Reasoning>(message.Contents[0]).Thought);
        Assert.Equal("Here is the answer.", Assert.IsType<Text>(message.Contents[1]).Value);

        var meta = message.Get<MessageMetadata>();
        Assert.NotNull(meta);
        Assert.Equal("msg_123", meta.Id);
        Assert.Equal("gpt-4o", meta.Model);
        Assert.Equal("stop", meta.FinishReason);
        Assert.Equal(10, meta.Usage?.InputTokens);
        Assert.Equal(20, meta.Usage?.OutputTokens);
        Assert.Equal(30, meta.Usage?.TotalTokens);
    }

    [Fact]
    public void StreamingMessage_PreservesInterleavedOrder()
    {
        var message = new StreamingMessage(Role.Assistant);
        message.Push(new TextStart(0));
        message.Push(new TextDelta(0, "First text. "));
        message.Push(new ToolCallStart(1, "call_1", "Search"));
        message.Push(new ToolCallDelta(1, "{\"q\": \"c#\"}"));
        message.Push(new TextStart(2));
        message.Push(new TextDelta(2, "Second text."));
        message.Push(new MessageEnd());

        Assert.Equal(3, message.Contents.Count);
        Assert.Equal("First text. ", Assert.IsType<Text>(message.Contents[0]).Value);
        Assert.Equal("call_1", Assert.IsType<ToolCall>(message.Contents[1]).Id);
        Assert.Equal("Second text.", Assert.IsType<Text>(message.Contents[2]).Value);
    }
}
