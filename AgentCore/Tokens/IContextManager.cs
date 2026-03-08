using AgentCore.Chat;
using AgentCore.LLM;

namespace AgentCore.Tokens;

public interface IContextManager
{
    Task<IList<Message>> ReduceAsync(IList<Message> messages, LLMOptions options, CancellationToken ct = default);
}
