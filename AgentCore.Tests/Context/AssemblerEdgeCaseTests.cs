using System;
using System.Collections.Generic;
using System.Linq;
using AgentCore.Context.Primitives;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using Xunit;

namespace AgentCore.Tests.Context;

public class AssemblerEdgeCaseTests
{
    private static MessageDelta D(IContentEvent content) => new(Content: content);

    [Fact]
    public void Assembler_MultipleTextDeltasSameBlock_ConcatenatesProperly()
    {
        var assembler = new Assembler();
        assembler.Push(D(new TextStart(0)));
        assembler.Push(D(new TextDelta(0, "Part 1, ")));
        assembler.Push(D(new TextDelta(0, "Part 2, ")));
        assembler.Push(D(new TextDelta(0, "Part 3.")));
        assembler.Push(D(new TextEnd(0)));

        var msg = assembler.ToMessage(new MessageEnd());
        var text = Assert.Single(msg.Contents.OfType<Text>());
        Assert.Equal("Part 1, Part 2, Part 3.", text.Value);
    }

    [Fact]
    public void Assembler_EmptyOrWhitespaceToolCallArgs_DefaultsSafely()
    {
        var assembler = new Assembler();
        assembler.Push(D(new ToolCallStart(0, "call_99", "NoArgTool")));
        assembler.Push(D(new ToolCallDelta(0, "")));
        assembler.Push(D(new ToolCallEnd(0)));

        var msg = assembler.ToMessage(new MessageEnd());
        var call = Assert.Single(msg.Contents.OfType<ToolCall>());
        Assert.Equal("call_99", call.Id);
        Assert.Equal("NoArgTool", call.Name);
        Assert.NotNull(call.Arguments);
    }

    [Fact]
    public void Assembler_AbruptStreamTermination_FinalizesPartialBlocksWithoutCrashing()
    {
        var assembler = new Assembler();
        assembler.Push(D(new TextStart(0)));
        assembler.Push(D(new TextDelta(0, "Abruptly interrupted text")));
        // No TextEnd and No MessageEnd sent (simulating network disconnection)

        var msg = assembler.ToMessage();
        var text = Assert.Single(msg.Contents.OfType<Text>());
        Assert.Equal("Abruptly interrupted text", text.Value);
    }

    [Fact]
    public void Assembler_InterleavedReasoningAndToolCalls_PreservesExactBlockOrder()
    {
        var assembler = new Assembler();
        assembler.Push(D(new ReasoningStart(0)));
        assembler.Push(D(new ReasoningDelta(0, "Deciding to query user details...")));
        assembler.Push(D(new ReasoningEnd(0)));

        assembler.Push(D(new ToolCallStart(1, "c1", "GetUser")));
        assembler.Push(D(new ToolCallDelta(1, "{\"id\":101}")));
        assembler.Push(D(new ToolCallEnd(1)));

        assembler.Push(D(new TextStart(2)));
        assembler.Push(D(new TextDelta(2, "Fetching user profile...")));
        assembler.Push(D(new TextEnd(2)));

        var msg = assembler.ToMessage(new MessageEnd());
        Assert.Equal(3, msg.Contents.Count);
        Assert.IsType<Reasoning>(msg.Contents[0]);
        Assert.IsType<ToolCall>(msg.Contents[1]);
        Assert.IsType<Text>(msg.Contents[2]);
    }
}
