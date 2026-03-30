namespace AgentCore.Conversation;

public class Message
{
    public Role Role { get; }
    public IReadOnlyList<IContent> Contents { get; }
    public MessageKind Kind { get; }

    public Message(Role role, IContent content, MessageKind kind = MessageKind.Default) 
        => (Role, Contents, Kind) = (role, [content], kind);

    public Message(Role role, IReadOnlyList<IContent> contents, MessageKind kind = MessageKind.Default) 
        => (Role, Contents, Kind) = (role, contents, kind);
}
