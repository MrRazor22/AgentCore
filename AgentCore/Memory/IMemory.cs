using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;
using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.Memory;

public interface IMemory
{
    Task<List<Message>> PrepareAsync(Message newInput, CancellationToken ct = default);
    Task RememberAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
    Task RestoreAsync(IReadOnlyList<Message> history, CancellationToken ct = default);
}


