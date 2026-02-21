using AgentCore.LLM.Protocol;
using AgentCore.Tokens;

namespace AgentCore.LLM.Handlers;

public sealed class TokenUsageHandler(ITokenManager _tokenManager) : IChunkHandler
{
    public StreamKind Kind => StreamKind.Usage;
    private TokenUsage? _usage;
    private string? _requestPayload;

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
        => response.TokenUsage = _tokenManager.ResolveAndRecord(_requestPayload!, response.ToCountablePayload(), _usage);
}
