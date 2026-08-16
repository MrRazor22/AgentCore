using AgentCore.Layers.LLM;
using AgentCore.LLM.Chat;
using Xunit;

namespace AgentCore.Tests;

public class MessageMergingLayerTests
{
    [Fact]
    public void MergeTextMessages_AdjacentUserTextMessages_MergesIntoSingleMessage()
    {
        var input = new List<Message>
        {
            new Message(Role.User, [new Text("help me understand architecture")]),
            new Message(Role.User, [new Text("no i mean the class diagram")])
        };

        var output = MessageMergingLayer.MergeTextMessages(input);

        Assert.Single(output);
        Assert.Equal(Role.User, output[0].Role);
        Assert.Equal("help me understand architecture\nno i mean the class diagram", ((Text)output[0].Contents[0]).Value);
        
        // Verify original input messages were NOT mutated
        Assert.Equal(2, input.Count);
        Assert.Equal("help me understand architecture", ((Text)input[0].Contents[0]).Value);
    }

    [Fact]
    public void MergeTextMessages_ToolCallAndToolResultSequences_PreservedUnchanged()
    {
        var toolCall = new ToolCall("1", "Search", new System.Text.Json.Nodes.JsonObject());
        var toolResult = new ToolResult("1", new Text("search result content"));

        var input = new List<Message>
        {
            new Message(Role.User, [new Text("find files")]),
            new Message(Role.Assistant, [toolCall]),
            new Message(Role.Tool, [toolResult]),
            new Message(Role.Assistant, [new Text("here are the files")])
        };

        var output = MessageMergingLayer.MergeTextMessages(input);

        Assert.Equal(4, output.Count);
        Assert.Equal(Role.User, output[0].Role);
        Assert.Equal(Role.Assistant, output[1].Role);
        Assert.Equal(Role.Tool, output[2].Role);
        Assert.Equal(Role.Assistant, output[3].Role);
    }

    [Fact]
    public void MergeTextMessages_MixedStructuredAndTextMessage_DoesNotMerge()
    {
        var toolCall = new ToolCall("1", "Search", new System.Text.Json.Nodes.JsonObject());

        var input = new List<Message>
        {
            new Message(Role.Assistant, [toolCall]),
            new Message(Role.Assistant, [new Text("I'm searching for files.")])
        };

        var output = MessageMergingLayer.MergeTextMessages(input);

        Assert.Equal(2, output.Count);
    }
}
