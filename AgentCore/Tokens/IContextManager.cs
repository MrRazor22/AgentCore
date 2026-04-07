using AgentCore.Conversation;
using AgentCore.LLM;

namespace AgentCore.Tokens;

public interface IContextCompactor
{
    Task<List<Message>> ReduceAsync(List<Message> chat, int totalTokens, LLMOptions options, CancellationToken ct = default);
}
