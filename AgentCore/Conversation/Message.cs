namespace AgentCore.Conversation;

public class Message
{
    public Role Role { get; }
    public IContent Content { get; }

    public Message(Role role, IContent content) { Role = role; Content = content; }
}
