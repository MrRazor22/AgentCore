using AgentCore.Chat;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AgentCore.Runtime;

public interface IAgentMemory
{
    Task<IList<Message>> RecallAsync(string sessionId, string userRequest);
    Task UpdateAsync(string sessionId, string userRequest, string response);
    Task ClearAsync(string sessionId);
}

public sealed class FileMemoryOptions
{
    public string? PersistDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentCore");
    public int MaxChatHistory { get; set; }
}

public sealed class FileMemory(IOptions<FileMemoryOptions> options) : IAgentMemory
{
    private readonly FileMemoryOptions _options = options?.Value ?? new FileMemoryOptions();
    private string? _cachedSessionId;
    private IList<Message>? _cached;

    public FileMemory() : this(null) { }

    public Task<IList<Message>> RecallAsync(string sessionId, string userRequest)
    {
        if (_options.PersistDir == null) return Task.FromResult<IList<Message>>(new List<Message>());

        if (_cachedSessionId == sessionId && _cached != null)
            return Task.FromResult(_cached);

        _cachedSessionId = sessionId;
        _cached = new List<Message>();

        var file = Path.Combine(_options.PersistDir, sessionId + ".json");
        if (!File.Exists(file)) return Task.FromResult(_cached);

        var json = File.ReadAllText(file);
        _cached = JsonSerializer.Deserialize<List<Message>>(json) ?? new List<Message>();
        return Task.FromResult(_cached);
    }

    public Task UpdateAsync(string sessionId, string userRequest, string response)
    {
        if (_options.PersistDir == null) return Task.CompletedTask;

        if (_cachedSessionId != sessionId || _cached == null)
            _cached = RecallAsync(sessionId, userRequest).Result;

        _cached.AddUser(userRequest);
        _cached.AddAssistant(response);
        TrimHistory(_cached);

        var file = Path.Combine(_options.PersistDir, sessionId + ".json");
        File.WriteAllText(file, _cached.ToJson());

        return Task.CompletedTask;
    }

    public Task ClearAsync(string sessionId)
    {
        if (_cachedSessionId == sessionId) { _cached = null; _cachedSessionId = null; }

        if (_options.PersistDir == null) return Task.CompletedTask;

        var file = Path.Combine(_options.PersistDir, sessionId + ".json");
        if (File.Exists(file)) File.Delete(file);

        return Task.CompletedTask;
    }

    private void TrimHistory(IList<Message> convo)
    {
        if (_options.MaxChatHistory <= 0) return;
        while (convo.Count > _options.MaxChatHistory)
            convo.RemoveAt(0);
    }
}
