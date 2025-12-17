using AgentCore.Chat;
using AgentCore.LLM.Client;
using AgentCore.LLM.Handlers;
using AgentCore.LLM.Protocol;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentCore.Tests.LLM
{
    public sealed class TextHandlerTests
    {
        private readonly TextHandler _handler;

        public TextHandlerTests()
        {
            var tools = new ToolRegistryCatalog();
            tools.Register((int x) => x + 1);

            var parser = new ToolCallParser(tools);
            _handler = new TextHandler(parser, Mock.Of<ILogger<TextHandler>>());
        }

        [Fact]
        public void Streamed_Text_Forms_ToolCall_Triggers_EarlyStop_And_Strips_Text()
        {
            _handler.OnChunk(Text("before "));
            _handler.OnChunk(Text(@"{""name"":""Invoke"","));
            _handler.OnChunk(Text(@"""arguments"":"));
            _handler.OnChunk(Text(@"{""x"":1}"));

            Assert.Throws<EarlyStopException>(() =>
                _handler.OnChunk(Text("} after")));

            var resp = _handler.OnResponse(FinishReason.ToolCall);

            Assert.Equal("before", resp.AssistantMessage);
            Assert.NotNull(resp.ToolCall);
            Assert.Equal("Invoke", resp.ToolCall!.Name);
        }

        [Fact]
        public void Streamed_Text_With_No_ToolCall_Returns_Full_Text()
        {
            _handler.OnChunk(Text("hello "));
            _handler.OnChunk(Text("world"));

            var resp = _handler.OnResponse(FinishReason.Stop);

            Assert.Equal("hello world", resp.AssistantMessage);
            Assert.Null(resp.ToolCall);
        }

        private static LLMStreamChunk Text(string s)
            => new LLMStreamChunk(StreamKind.Text, s);
    }
}
