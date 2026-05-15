using AgentCore.Conversation;

namespace AgentCore.Memory;

public interface IContextAssembler
{
    IReadOnlyList<Message> Assemble(
        IReadOnlyList<Message> memory,
        IReadOnlyList<Message> conversation,
        int tokenBudget);
}