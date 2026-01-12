using AgentCore.LLM.Protocol;

namespace AgentCore.LLM.Handlers
{
    public sealed class FinishHandler : IChunkHandler
    {
        public StreamKind Kind => StreamKind.Finish;

        private FinishReason _finish;

        public void OnRequest(LLMRequest request)
        {
            _finish = FinishReason.Stop;
        }

        public void OnChunk(LLMStreamChunk chunk)
        {
            if (chunk.Kind != StreamKind.Finish)
                return;

            _finish = chunk.AsFinishReason() ?? FinishReason.Stop;
        }

        public void OnResponse(LLMResponse response)
        {
            response.FinishReason = _finish;
        }
    }
}
