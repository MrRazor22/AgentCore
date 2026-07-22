using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tools;
using System.Text;
using System.Text.Json.Nodes;

namespace AgentCore.Context;

public interface IContext
{
    IReadOnlyList<Message> Messages { get; }
    Task<List<Message>> PrepareAsync(Message newInput, CancellationToken ct = default);
    Task UpdateAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
    Task RestoreAsync(IReadOnlyList<Message> history, CancellationToken ct = default);
}


