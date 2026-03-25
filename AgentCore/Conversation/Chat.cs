namespace AgentCore.Conversation;

public sealed class Chat
{
    private readonly List<Turn> _turns = [];
    public IReadOnlyList<Turn> Turns => _turns;

    internal void Add(Turn turn) => _turns.Add(turn);

    public IList<Message> ToMessages(
        IReadOnlyList<Message>? system = null,
        Message? summary = null)
    {
        var result = new List<Message>();
        if (system != null) result.AddRange(system);
        if (summary != null) result.Add(summary);
        foreach (var turn in _turns)
        {
            result.Add(turn.User);
            foreach (var (call, resultMsg) in turn.ToolSteps) { result.Add(call); result.Add(resultMsg); }
            if (turn.AssistantReply != null) result.Add(turn.AssistantReply);
        }
        return result;
    }
}
