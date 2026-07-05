using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using AgentCore.Conversation;

namespace AgentCore.Tests;

public class SerializationTests
{
    [Fact]
    public void Message_SerializationDeserialization_PreservesPolymorphicContent()
    {
        // Arrange
        var message = new Message
        {
            Role = Role.Assistant,
            Contents = new List<IContent>
            {
                new Text("Hello world"),
                new Reasoning("Thinking process..."),
                new ToolCall("call_123", "search", new JsonObject { ["query"] = "test" }, IsApproved: true),
                new ToolResult("call_123", new Text("Search finished"))
            },
            Metadata = new Dictionary<string, object> { ["trace_id"] = "abc-123" }
        };

        // Act
        var json = JsonSerializer.Serialize(message);
        var deserialized = JsonSerializer.Deserialize<Message>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(Role.Assistant, deserialized.Role);
        Assert.Equal(4, deserialized.Contents.Count);

        var textContent = Assert.IsType<Text>(deserialized.Contents[0]);
        Assert.Equal("Hello world", textContent.Value);

        var reasoningContent = Assert.IsType<Reasoning>(deserialized.Contents[1]);
        Assert.Equal("Thinking process...", reasoningContent.Thought);

        var toolCallContent = Assert.IsType<ToolCall>(deserialized.Contents[2]);
        Assert.Equal("call_123", toolCallContent.Id);
        Assert.Equal("search", toolCallContent.Name);
        Assert.True(toolCallContent.IsApproved);
        Assert.Equal("test", toolCallContent.Arguments["query"]?.ToString());

        var toolResultContent = Assert.IsType<ToolResult>(deserialized.Contents[3]);
        Assert.Equal("call_123", toolResultContent.CallId);
        Assert.NotNull(toolResultContent.Result);
        var innerResult = Assert.IsType<Text>(toolResultContent.Result);
        Assert.Equal("Search finished", innerResult.Value);
    }
}
