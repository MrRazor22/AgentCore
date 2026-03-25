namespace AgentCore.Conversation;

public sealed class Turn
{
    public Message User { get; }
    public IReadOnlyList<(Message Call, Message Result)> ToolSteps { get; }
    public Message? AssistantReply { get; }

    public Turn(Message user,
        IReadOnlyList<(Message, Message)>? steps = null,
        Message? reply = null)
    {
        User = user;
        ToolSteps = steps ?? [];
        AssistantReply = reply;
    }
}
