using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Tornado;
using AgentCore.Tool;
using LlmTornado;
using LlmTornado.Chat;
using LlmTornado.Chat.Models;
using LlmTornado.Code;
using Xunit;
using ToolCall = AgentCore.LLM.Chat.ToolCall;

namespace AgentCore.Tests;

public class TornadoTests
{
    [Fact]
    public void ToTornadoRole_MapsCorrectly()
    {
        Assert.Equal(ChatMessageRoles.System, Role.System.ToTornadoRole());
        Assert.Equal(ChatMessageRoles.User, Role.User.ToTornadoRole());
        Assert.Equal(ChatMessageRoles.Assistant, Role.Assistant.ToTornadoRole());
        Assert.Equal(ChatMessageRoles.Tool, Role.Tool.ToTornadoRole());
    }

    [Fact]
    public void ToTornadoMessage_ConvertsTextAndReasoning()
    {
        var msg = new Message(Role.Assistant, [
            new Reasoning("Thinking step"),
            new Text("Final answer")
        ]);

        var tornadoMsg = msg.ToTornadoMessage();

        Assert.Equal(ChatMessageRoles.Assistant, tornadoMsg.Role);
        Assert.Equal("Thinking step", tornadoMsg.Reasoning);
        Assert.Equal("Final answer", tornadoMsg.Content);
    }

    [Fact]
    public void ToTornadoMessage_ConvertsToolCallAndResult()
    {
        var tcMsg = new Message(Role.Assistant, [
            new ToolCall("call_1", "calc", "{\"a\":5}")
        ]);

        var tornadoTc = tcMsg.ToTornadoMessage();
        Assert.NotNull(tornadoTc.ToolCalls);
        var tc = Assert.Single(tornadoTc.ToolCalls);
        Assert.Equal("call_1", tc.Id);
        Assert.Equal("calc", tc.FunctionCall?.Name);
        Assert.Contains("5", tc.FunctionCall?.Arguments);

        var trMsg = new Message(Role.Tool, [
            new ToolResult("call_1", [new Text("result_10")])
        ], id: "call_1");

        var tornadoTr = trMsg.ToTornadoMessage();
        Assert.Equal(ChatMessageRoles.Tool, tornadoTr.Role);
        Assert.Equal("call_1", tornadoTr.ToolCallId);
        Assert.Equal("result_10", tornadoTr.Content);
    }

    [Fact]
    public void ToTornadoMessage_ConvertsMultimodalParts()
    {
        var msg = new Message(Role.User, [
            new Text("Analyze audio and video"),
            new Audio(new byte[] { 1, 2, 3 }, MediaType: "audio/wav"),
            new Video(Uri: new Uri("https://example.com/video.mp4"), MediaType: "video/mp4"),
            new Image(Uri: new Uri("https://example.com/image.png"))
        ]);

        var tornadoMsg = msg.ToTornadoMessage();
        Assert.NotNull(tornadoMsg.Parts);
        Assert.Equal(4, tornadoMsg.Parts.Count);
        Assert.Equal(ChatMessageTypes.Text, tornadoMsg.Parts[0].Type);
        Assert.Equal(ChatMessageTypes.Audio, tornadoMsg.Parts[1].Type);
        Assert.Equal(ChatMessageTypes.FileLink, tornadoMsg.Parts[2].Type);
        Assert.Equal(ChatMessageTypes.Image, tornadoMsg.Parts[3].Type);
    }

    [Fact]
    public void WithTornado_RegistersProvider()
    {
        var api = new TornadoApi("test_key");
        var builder = Agent.Create().WithTornado(api, ChatModel.OpenAi.Gpt4.O);

        var agent = builder.Build();
        Assert.NotNull(agent);
        var llm = agent.LLM;
        Assert.NotNull(llm);
        Assert.IsType<TornadoLLM>(llm);
    }
}
