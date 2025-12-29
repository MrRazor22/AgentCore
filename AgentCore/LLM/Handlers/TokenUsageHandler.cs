using AgentCore.LLM.Protocol;
using AgentCore.Tokens;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgentCore.LLM.Handlers
{
    public sealed class TokenUsageHandler : IChunkHandler
    {
        public StreamKind Kind => StreamKind.Usage;

        private TokenUsage? _usage;

        public void OnRequest(LLMRequest request)
        {
            _usage = null;
        }

        public void OnChunk(LLMStreamChunk chunk)
        {
            _usage = chunk.AsTokenUsage();
        }

        public void OnResponse(LLMResponse response)
        {
            if (_usage != null)
                response.TokenUsage = _usage;
        }
    }

}
