using AgentCore.Chat;

namespace AgentCore.Runtime;
public interface IAgentContext
{
    string SessionId { get; }
    AgentConfig Config { get; }
    IList<Message> Messages { get; }
    string UserInput { get; }
    Type? OutputType { get; }
    IServiceProvider Services { get; }
    CancellationToken CancellationToken { get; }
}

public sealed class AgentContext(
    string sessionId,
    AgentConfig config,
    IServiceProvider services,
    string userInput,
    Type? outputType,
    CancellationToken cancellationToken
) : IAgentContext
{
    public string SessionId => sessionId;
    public AgentConfig Config => config;
    public IServiceProvider Services => services;
    public string UserInput => userInput;
    public Type? OutputType => outputType;
    public CancellationToken CancellationToken => cancellationToken;
    public IList<Message> Messages { get; } = new List<Message>();
} 
