using AgentCore.LLM.Protocol;
using AgentCore.Tokens;

namespace AgentCore.LLM.Handlers
{
    public sealed class TokenUsageHandler : IChunkHandler
    {
        public StreamKind Kind => StreamKind.Usage;

        private TokenUsage? _usage;

        public void OnRequest<T>(LLMRequest<T> request)
        {
            _usage = null;
        }

        public void OnChunk(LLMStreamChunk chunk)
        {
            if (chunk.Kind != StreamKind.Usage)
                return;

            _usage = chunk.AsTokenUsage();
        }

        public void OnResponse<T>(LLMResponse<T> response)
        {
            if (_usage != null)
                response.TokenUsage = _usage;
        }
    }
}
