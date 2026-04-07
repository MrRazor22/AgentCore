using AgentCore.Tooling;
using System.ComponentModel;

namespace AgentCore.Context;

/// <summary>
/// Tools that allow the agent to interact with its persistent knowledge memory.
/// </summary>
public sealed class KnowledgeTools
{
    private readonly IMemory _memory;

    public KnowledgeTools(IMemory memory)
    {
        _memory = memory;
    }

    [Description("Stores a piece of information to be remembered in future turns or sessions.")]
    public async Task<string> Remember(
        [Description("The unique key/topic of the information")] string key,
        [Description("The actual information to store")] string value)
    {
        await _memory.RememberAsync(key.ToLowerInvariant(), value);
        return $"Memory updated: {key}";
    }

    [Description("Recalls a specifically named piece of information from memory.")]
    public async Task<string> Recall(
        [Description("The key/topic to look up")] string key)
    {
        var result = await _memory.RecallAsync(key.ToLowerInvariant());
        return result ?? $"No memory found for '{key}'.";
    }

    [Description("Forgets a piece of information from memory.")]
    public async Task<string> Forget(
        [Description("The key/topic to forget")] string key)
    {
        await _memory.ForgetAsync(key.ToLowerInvariant());
        return $"Memory removed: {key}";
    }
}
