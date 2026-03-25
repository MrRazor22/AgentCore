using AgentCore.Conversation;

namespace AgentCore.Runtime;
public interface IAgentContext
{
    string SessionId { get; }
    AgentConfig Config { get; }
    Chat Chat { get; set; }
    string UserInput { get; }
    Type? OutputType { get; }
    CancellationToken CancellationToken { get; }
}

public sealed class AgentContext(
    string sessionId,
    AgentConfig config,
    string userInput,
    Type? outputType,
    CancellationToken cancellationToken
) : IAgentContext
{
    public string SessionId => sessionId;
    public AgentConfig Config => config;
    public string UserInput => userInput;
    public Type? OutputType => outputType;
    public CancellationToken CancellationToken => cancellationToken;
    public Chat Chat { get; set; } = new();
}
