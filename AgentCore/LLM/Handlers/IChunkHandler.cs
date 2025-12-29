using AgentCore.LLM.Protocol;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgentCore.LLM.Handlers
{
    public interface IChunkHandler
    {
        StreamKind Kind { get; }
        void OnRequest(LLMRequest request);
        void OnChunk(LLMStreamChunk chunk);
        void OnResponse(LLMResponse response);
    }
}
