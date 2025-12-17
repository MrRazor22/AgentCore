using AgentCore.Chat;
using AgentCore.LLM.Handlers;
using AgentCore.LLM.Protocol;
using AgentCore.LLM.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AgentCore.Tests.LLM
{
    public sealed class StructuredHandler_Tests
    {
        private readonly StructuredHandler _handler;

        public StructuredHandler_Tests()
        {
            _handler = new StructuredHandler(Mock.Of<ILogger<StructuredHandler>>());
        }

        [Fact]
        public void Streamed_Json_Forms_Structured_Response()
        {
            var convo = new Conversation();
            var req = new LLMStructuredRequest(
                prompt: convo,
                resultType: typeof(ResultDto)
            );

            _handler.OnRequest(req);

            // 🔴 MUST match schema casing
            _handler.OnChunk(Text(@"{""Value"":"));
            _handler.OnChunk(Text(" 42"));
            _handler.OnChunk(Text("}"));

            var resp = _handler.OnResponse(FinishReason.Stop);

            var structured = Assert.IsType<LLMStructuredResponse>(resp);
            var result = Assert.IsType<ResultDto>(structured.Result);

            Assert.Equal(42, result.Value);
        }

        [Fact]
        public void Empty_Response_Throws_RetryException()
        {
            var req = new LLMStructuredRequest(
                prompt: new Conversation(),
                resultType: typeof(ResultDto)
            );

            _handler.OnRequest(req);

            Assert.Throws<RetryException>(() =>
                _handler.OnResponse(FinishReason.Stop));
        }

        [Fact]
        public void Cancelled_Returns_Null_Structured_Response()
        {
            var req = new LLMStructuredRequest(
                prompt: new Conversation(),
                resultType: typeof(ResultDto)
            );

            _handler.OnRequest(req);

            var resp = _handler.OnResponse(FinishReason.Cancelled);

            var structured = Assert.IsType<LLMStructuredResponse>(resp);
            Assert.Null(structured.Result);
        }

        private static LLMStreamChunk Text(string s)
            => new LLMStreamChunk(StreamKind.Text, s);

        private sealed class ResultDto
        {
            public int Value { get; set; }
        }
    }
}
