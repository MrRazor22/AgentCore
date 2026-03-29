namespace AgentCore.Conversation;

public class Message
{
    public Role Role { get; }
    public IReadOnlyList<IContent> Contents { get; }

    public Message(Role role, IContent content) => (Role, Contents) = (role, [content]);

    public Message(Role role, IReadOnlyList<IContent> contents) => (Role, Contents) = (role, contents);
}
