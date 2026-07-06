using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.Conversation;
using AgentCore.Memory;
using AgentCore.Tokens;
using AgentCore.Tooling;

namespace TestApp.Decorators;

public class DurableFileMemory : IMemory
{
    private readonly IMemory _innerMemory;
    private readonly string _sessionId;
    private readonly string _filePath;

    public DurableFileMemory(IMemory innerMemory, string sessionId, string sessionsDir = "sessions")
    {
        _innerMemory = innerMemory;
        _sessionId = sessionId;
        _filePath = Path.GetFullPath(Path.Combine(sessionsDir, $"{sessionId}.json"));
        
        var dir = Path.GetDirectoryName(_filePath);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public async Task RememberAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
    {
        await _innerMemory.RememberAsync(completedTurn, ct).ConfigureAwait(false);

        // Recall everything (budget 0 retrieves all history from ChatMemory)
        var fullHistory = await _innerMemory.RecallAsync(
            new Message(Role.User, new Text("")), 
            new TokenBudget(0), 
            ct).ConfigureAwait(false);

        var json = JsonSerializer.Serialize(fullHistory, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(_filePath, json, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Message>> RecallAsync(Message currentInput, TokenBudget budget, CancellationToken ct = default)
    {
        if (File.Exists(_filePath))
        {
            var existing = await _innerMemory.RecallAsync(new Message(Role.User, new Text("")), new TokenBudget(0), ct).ConfigureAwait(false);
            if (existing.Count == 0)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
                    var messages = JsonSerializer.Deserialize<List<Message>>(json);
                    if (messages != null && messages.Count > 0)
                    {
                        await _innerMemory.RememberAsync(messages, ct).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Ignore load/parse errors
                }
            }
        }

        return await _innerMemory.RecallAsync(currentInput, budget, ct).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _innerMemory.ClearAsync(ct).ConfigureAwait(false);
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }
}
