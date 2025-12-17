using AgentCore.LLM.Protocol;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgentCore.LLM.Handlers
{
    public interface IChunkHandler
    {
        void OnRequest(LLMRequest request);
        void OnChunk(LLMStreamChunk chunk);
        LLMResponse OnResponse(FinishReason finishReason);
    }
}
