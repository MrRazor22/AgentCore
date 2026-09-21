using System.Collections.Generic;
using AgentCore.Context.Primitives;
using AgentCore.LLM.Chat;
using Xunit;

namespace AgentCore.Tests;

public class MessageNormalizerTests
{
    [Fact]
    public void ChatNormalizer_AdjacentUserTextMessages_MergesIntoSingleMessage()
    {
        var input = new List<Message>
        {
            new Message(Role.User, [new Text("help me understand architecture")]),
            new Message(Role.User, [new Text("no i mean the class diagram")])
        };

        var normalizer = new ChatNormalizer();
        var output = normalizer.Normalize(input);

        Assert.Single(output);
        Assert.Equal(Role.User, output[0].Role);
        Assert.Equal("help me understand architecture\nno i mean the class diagram", ((Text)output[0].Contents[0]).Value);
    }

    [Fact]
    public void ChatNormalizer_ToolCallAndToolResultSequences_PreservedUnchanged()
    {
        var toolCall = new ToolCall("1", "Search");
        var input = new List<Message>
        {
            new Message(Role.User, [new Text("find files")]),
            new Message(Role.Assistant, [toolCall]),
            new Message(Role.Tool, [new ToolResult("1", [new Text("search result content")])]),
            new Message(Role.Assistant, [new Text("here are the files")])
        };

        var normalizer = new ChatNormalizer();
        var output = normalizer.Normalize(input);

        Assert.Equal(4, output.Count);
        Assert.Equal(Role.User, output[0].Role);
        Assert.Equal(Role.Assistant, output[1].Role);
        Assert.Equal(Role.Tool, output[2].Role);
        Assert.Equal(Role.Assistant, output[3].Role);
    }

    [Fact]
    public void ChatNormalizer_OrphanToolResult_IsPurged()
    {
        var input = new List<Message>
        {
            new Message(Role.User, [new Text("find files")]),
            new Message(Role.Tool, [new ToolResult("non_existent", [new Text("orphan result")])])
        };

        var normalizer = new ChatNormalizer();
        var output = normalizer.Normalize(input);

        Assert.Single(output);
        Assert.Equal(Role.User, output[0].Role);
    }

    [Fact]
    public void ChatNormalizer_DanglingToolCall_IsPairedWithAbortedToolResult()
    {
        var toolCall = new ToolCall("call_1", "Search");
        var input = new List<Message>
        {
            new Message(Role.User, [new Text("find files")]),
            new Message(Role.Assistant, [toolCall])
        };

        var normalizer = new ChatNormalizer();
        var output = normalizer.Normalize(input);

        Assert.Equal(3, output.Count);
        Assert.Equal(Role.Tool, output[2].Role);
        Assert.Equal("call_1", output[2].Contents.OfType<ToolResult>().First().ToolCallId);
    }
}
