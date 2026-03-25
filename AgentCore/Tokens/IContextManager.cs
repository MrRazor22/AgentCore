using AgentCore.Conversation;
using AgentCore.LLM;

namespace AgentCore.Tokens;

public interface IContextManager
{
    Task<Chat> ReduceAsync(Chat chat, int totalTokens, LLMOptions options, CancellationToken ct = default);
}
