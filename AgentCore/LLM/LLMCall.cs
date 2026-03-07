using AgentCore.Chat;

namespace AgentCore.LLM;

public sealed record LLMCall(IReadOnlyList<Message> Messages, LLMOptions Options);
