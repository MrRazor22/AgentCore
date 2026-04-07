using AgentCore.Conversation;

namespace AgentCore.Context;

/// <summary>
/// A minimal implementation of IContextAssembler that only handles a single system prompt if provided.
/// </summary>
public sealed class SimpleContextAssembler : IContextAssembler
{
    private readonly List<ContextRegistration> _registrations = [];

    public void Register(IContextSource source, int? maxTokenBudget = null)
    {
        _registrations.Add(new ContextRegistration(source, maxTokenBudget));
    }

    public async Task<IReadOnlyList<Message>> AssembleAsync(int availableTokens, CancellationToken ct = default)
    {
        var messages = new List<Message>();
        
        foreach (var reg in _registrations)
        {
            var contents = await reg.Source.GetContextAsync(ct);
            if (contents.Count > 0)
            {
                messages.Add(new Message(reg.Source.Role, contents));
            }
        }
        
        return messages;
    }
}
