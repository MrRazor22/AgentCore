using AgentCore.LLM.Protocol;
using AgentCore.Tokens;

namespace AgentCore.LLM.Handlers
{
    public sealed class TokenUsageHandler : IChunkHandler
    {
        public StreamKind Kind => StreamKind.Usage;

        private readonly ITokenManager _tokenManager;
        private TokenUsage? _usage;
        private string? _requestPayload;

        public TokenUsageHandler(ITokenManager tokenManager)
        {
            _tokenManager = tokenManager;
        }

        public void OnRequest<T>(LLMRequest<T> request)
        {
            _usage = null;
            _requestPayload = request.ToString();
        }

        public void OnChunk(LLMStreamChunk chunk)
        {
            if (chunk.Kind == StreamKind.Usage)
                _usage = chunk.AsTokenUsage();
        }

        public void OnResponse<T>(LLMResponse<T> response)
        {
            var resolved = _tokenManager.ResolveAndRecord(
                _requestPayload,
                response.ToString(),
                _usage
            );

            response.TokenUsage = resolved;
        }
    }
}
