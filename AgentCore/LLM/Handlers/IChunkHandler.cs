using AgentCore.LLM.Protocol;

namespace AgentCore.LLM.Handlers;

public interface IChunkHandler
{
    StreamKind Kind { get; }
    void OnRequest(LLMRequest request);
    void OnChunk(LLMStreamChunk chunk);
    void OnResponse(LLMResponse response);
}
