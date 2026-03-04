using AgentCore.Chat;

namespace AgentCore.Runtime;
public interface IAgentContext
{
    string SessionId { get; }
    AgentConfig Config { get; }
    IList<Message> Messages { get; }
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
    public IList<Message> Messages { get; } = new List<Message>();
} 
