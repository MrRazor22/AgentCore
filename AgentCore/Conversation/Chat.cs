using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCore.Conversation;

/// <summary>
/// Stores and retrieves conversation history (List&lt;Message&gt;) per session.
/// This is the chat memory layer — separate from IMemory (cognitive/knowledge memory).
/// </summary>
public interface IChat
{
    Task<List<Message>> RecallAsync(string sessionId);
    Task UpdateAsync(string sessionId, List<Message> chat);
    Task ClearAsync(string sessionId);
    Task<IReadOnlyList<string>> GetAllSessionsAsync();
}

public sealed class ChatFileStoreOptions
{
    public string? PersistDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentCore");
}

public sealed class ChatFileStore(ChatFileStoreOptions? options, ILogger<ChatFileStore>? logger = null) : IChat
{
    private readonly ChatFileStoreOptions _options = options ?? new ChatFileStoreOptions();
    private readonly SemaphoreSlim[] _sessionLocks = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly ILogger<ChatFileStore> _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatFileStore>.Instance;

    public ChatFileStore() : this(null, null) { }
    public ChatFileStore(ChatFileStoreOptions? options) : this(options, null) { }

    private SemaphoreSlim GetLock(string sessionId)
        => _sessionLocks[Math.Abs(sessionId.GetHashCode()) % _sessionLocks.Length];

    public async Task<List<Message>> RecallAsync(string sessionId)
    {
        _logger.LogDebug("Chat file recall: SessionId={SessionId}", sessionId);

        if (_options.PersistDir == null) return new List<Message>();

        var file = Path.Combine(_options.PersistDir, sessionId + ".json");
        if (!File.Exists(file))
        {
            _logger.LogDebug("Chat file recall result: SessionId={SessionId} FileNotFound MessageCount=0", sessionId);
            return new List<Message>();
        }

        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _logger.LogDebug("Chat file recall: SessionId={SessionId} FilePath={FilePath}", sessionId, file);
            var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
            var messages = FromJson(json);
            _logger.LogDebug("Chat file recall result: SessionId={SessionId} MessageCount={MsgCount}", sessionId, messages.Count);
            return messages;
        }
        catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
        {
            _logger.LogWarning(ex, "Chat file recall: SessionId={SessionId} File not found", sessionId);
            return new List<Message>();
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public async Task UpdateAsync(string sessionId, List<Message> chat)
    {
        _logger.LogDebug("Chat file update: SessionId={SessionId} MessageCount={MsgCount}", sessionId, chat.Count);

        if (_options.PersistDir == null) return;

        Directory.CreateDirectory(_options.PersistDir);
        var file = Path.Combine(_options.PersistDir, sessionId + ".json");
        var tmpFile = file + ".tmp";

        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _logger.LogDebug("Chat file update: SessionId={SessionId} FilePath={FilePath}", sessionId, file);
            var json = ToJson(chat);
            await File.WriteAllTextAsync(tmpFile, json).ConfigureAwait(false);
            File.Move(tmpFile, file, overwrite: true);
            _logger.LogDebug("Chat file update result: SessionId={SessionId} Success", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat file update failed: SessionId={SessionId}", sessionId);
            throw;
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public async Task ClearAsync(string sessionId)
    {
        _logger.LogDebug("Chat file clear: SessionId={SessionId}", sessionId);

        if (_options.PersistDir == null) return;

        var file = Path.Combine(_options.PersistDir, sessionId + ".json");

        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(file))
            {
                _logger.LogDebug("Chat file clear: SessionId={SessionId} FilePath={FilePath}", sessionId, file);
                File.Delete(file);
                _logger.LogDebug("Chat file clear result: SessionId={SessionId} Success", sessionId);
            }
            else
            {
                _logger.LogDebug("Chat file clear result: SessionId={SessionId} FileNotFound", sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat file clear failed: SessionId={SessionId}", sessionId);
            throw;
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public Task<IReadOnlyList<string>> GetAllSessionsAsync()
    {
        if (_options.PersistDir == null || !Directory.Exists(_options.PersistDir))
        {
            _logger.LogDebug("Chat file get all sessions: SessionCount=0 (directory not configured or doesn't exist)");
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var files = Directory.GetFiles(_options.PersistDir, "*.json");
        var sessions = files
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToList();
        _logger.LogDebug("Chat file get all sessions: SessionCount={SessionCount}", sessions.Count);
        return Task.FromResult<IReadOnlyList<string>>(sessions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string ToJson(List<Message> chat) =>
        JsonSerializer.Serialize(chat, JsonOptions);

    private static List<Message> FromJson(string json) =>
        JsonSerializer.Deserialize<List<Message>>(json, JsonOptions) ?? new List<Message>();

}


