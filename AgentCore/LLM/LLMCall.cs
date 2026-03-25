using AgentCore.Conversation;

namespace AgentCore.LLM;

public sealed record LLMCall(IReadOnlyList<Message> Messages, LLMOptions Options);
