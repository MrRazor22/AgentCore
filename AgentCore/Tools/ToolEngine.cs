using AgentCore.Chat;
using AgentCore.LLM.Client;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgentCore.Tools
{
    public interface IToolEngine
    {
        void PrepareRequest(LLMRequestBase request);
        void HandleChunk(LLMStreamChunk chunk);
        ToolCall? GetToolCall(FinishReason finishReason);
    }

    internal class ToolEngine
    {
    }
}
