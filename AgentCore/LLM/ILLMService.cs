using AgentCore.LLM.Chat; 
using AgentCore.Tools;

namespace AgentCore.LLM;

public interface ILLMService
{ 
    IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        CancellationToken ct = default);
}
