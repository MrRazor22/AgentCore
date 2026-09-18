using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.Context;
using AgentCore.Layers.Context;
using AgentCore.LLM.Chat;
using Xunit;

namespace AgentCore.Tests;

public class MessageMergingLayerTests
{
    private class InMemoryContext(IReadOnlyList<Message> messages) : IContext
    {
        public Task<IReadOnlyList<Message>> PrepareAsync(IEnumerable<Message>? staged = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Message>>(staged != null ? [.. messages, .. staged] : messages);

        public IAsyncEnumerable<IContentEvent> WriteAsync(IAsyncEnumerable<IMessageEvent> events, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    [Fact]
    public async Task ChatGrammar_AdjacentUserTextMessages_MergesIntoSingleMessage()
    {
        var input = new List<Message>
        {
            new Message(Role.User, [new Text("help me understand architecture")]),
            new Message(Role.User, [new Text("no i mean the class diagram")])
        };

        var layer = new ChatGrammarLayer();
        layer.Attach(new InMemoryContext(input));

        var output = await layer.PrepareAsync();

        Assert.Single(output);
        Assert.Equal(Role.User, output[0].Role);
        Assert.Equal("help me understand architecture\nno i mean the class diagram", ((Text)output[0].Contents[0]).Value);
    }

    [Fact]
    public async Task ChatGrammar_ToolCallAndToolResultSequences_PreservedUnchanged()
    {
        var toolCall = new ToolCall("1", "Search");
        var input = new List<Message>
        {
            new Message(Role.User, [new Text("find files")]),
            new Message(Role.Assistant, [toolCall]),
            new Message(Role.Tool, [new Text("search result content")], metadata: [new ToolCallId("1")]),
            new Message(Role.Assistant, [new Text("here are the files")])
        };

        var layer = new ChatGrammarLayer();
        layer.Attach(new InMemoryContext(input));

        var output = await layer.PrepareAsync();

        Assert.Equal(4, output.Count);
        Assert.Equal(Role.User, output[0].Role);
        Assert.Equal(Role.Assistant, output[1].Role);
        Assert.Equal(Role.Tool, output[2].Role);
        Assert.Equal(Role.Assistant, output[3].Role);
    }

    [Fact]
    public async Task ChatGrammar_OrphanToolResult_IsPurged()
    {
        var input = new List<Message>
        {
            new Message(Role.User, [new Text("find files")]),
            new Message(Role.Tool, [new Text("orphan result")], metadata: [new ToolCallId("non_existent")])
        };

        var layer = new ChatGrammarLayer();
        layer.Attach(new InMemoryContext(input));

        var output = await layer.PrepareAsync();

        Assert.Single(output);
        Assert.Equal(Role.User, output[0].Role);
    }
}
