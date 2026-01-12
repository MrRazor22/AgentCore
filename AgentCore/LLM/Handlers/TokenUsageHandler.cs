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

        public void OnRequest(LLMRequest request)
        {
            _usage = null;
            _requestPayload = request.ToCountablePayload();
        }

        public void OnChunk(LLMStreamChunk chunk)
        {
            if (chunk.Kind == StreamKind.Usage)
                _usage = chunk.AsTokenUsage();
        }

        public void OnResponse(LLMResponse response)
        {
            var resolved = _tokenManager.ResolveAndRecord(
                _requestPayload,
                response.ToCountablePayload(),
                _usage
            );

            response.TokenUsage = resolved;
        }
    }
}
