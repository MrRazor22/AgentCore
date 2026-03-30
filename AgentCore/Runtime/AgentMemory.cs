using AgentCore.Conversation;
using Microsoft.Extensions.Logging; 
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCore.Runtime;

public interface IAgentMemory
{
    Task<List<Message>> RecallAsync(string sessionId);
    Task UpdateAsync(string sessionId, List<Message> chat);
    Task ClearAsync(string sessionId);
    Task<IReadOnlyList<string>> GetAllSessionsAsync();
}

public sealed class InMemoryMemory : IAgentMemory
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<Message>> _store = new();

    public Task<List<Message>> RecallAsync(string sessionId)
    {
        if (_store.TryGetValue(sessionId, out var chat))
        {
            var cloned = new List<Message>(chat);
            return Task.FromResult(cloned);
        }
        return Task.FromResult(new List<Message>());
    }

    public Task UpdateAsync(string sessionId, List<Message> chat)
    {
        var cloned = new List<Message>(chat);
        _store[sessionId] = cloned;
        return Task.CompletedTask;
    }

    public Task ClearAsync(string sessionId)
    {
        _store.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetAllSessionsAsync()
    {
        return Task.FromResult<IReadOnlyList<string>>(_store.Keys.ToList());
    }
}

public sealed class FileMemoryOptions
{
    public string? PersistDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentCore");
}

public sealed class FileMemory(FileMemoryOptions? options) : IAgentMemory
{
    private readonly FileMemoryOptions _options = options ?? new FileMemoryOptions();
    private readonly SemaphoreSlim[] _sessionLocks = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public FileMemory() : this(null) { }

    private SemaphoreSlim GetLock(string sessionId)
        => _sessionLocks[Math.Abs(sessionId.GetHashCode()) % _sessionLocks.Length];

    public async Task<List<Message>> RecallAsync(string sessionId)
    {
        if (_options.PersistDir == null) return new List<Message>();

        var file = Path.Combine(_options.PersistDir, sessionId + ".json");
        if (!File.Exists(file)) return new List<Message>();

        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
            return FromJson(json);
        }
        catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
        {
            return new List<Message>();
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public async Task UpdateAsync(string sessionId, List<Message> chat)
    {
        if (_options.PersistDir == null) return;

        Directory.CreateDirectory(_options.PersistDir);
        var file = Path.Combine(_options.PersistDir, sessionId + ".json");
        var tmpFile = file + ".tmp";

        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = ToJson(chat);
            await File.WriteAllTextAsync(tmpFile, json).ConfigureAwait(false);
            File.Move(tmpFile, file, overwrite: true);
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public async Task ClearAsync(string sessionId)
    {
        if (_options.PersistDir == null) return;

        var file = Path.Combine(_options.PersistDir, sessionId + ".json");

        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(file)) File.Delete(file);
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public Task<IReadOnlyList<string>> GetAllSessionsAsync()
    {
        if (_options.PersistDir == null || !Directory.Exists(_options.PersistDir))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var files = Directory.GetFiles(_options.PersistDir, "*.json");
        var sessions = files
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(sessions);
    }

    private static string ToJson(List<Message> chat)
    {
        var messages = chat.Select(m => new MessageDto
        {
            Role = m.Role.ToString(),
            Kind = m.Kind != MessageKind.Default ? m.Kind.ToString() : null,
            Content = m.Contents.Select(SerializeContent).ToList()
        }).ToList();

        return JsonSerializer.Serialize(messages, new JsonSerializerOptions { WriteIndented = true });
    }

    private static List<Message> FromJson(string json)
    {
        var chat = new List<Message>();
        try
        {
            var messages = JsonSerializer.Deserialize<List<MessageDto>>(json);
            if (messages == null) return chat;

            foreach (var dto in messages)
            {
                if (dto.Role != null)
                {
                    var contents = DeserializeContents(dto.Content);
                    var kind = MessageKind.Default;
                    if (!string.IsNullOrEmpty(dto.Kind) && Enum.TryParse<MessageKind>(dto.Kind, out var parsed))
                        kind = parsed;
                    chat.Add(new Message(Enum.Parse<Role>(dto.Role), contents, kind));
                }
            }
        }
        catch { }
        return chat;
    }

    private static List<IContent> DeserializeContents(object? obj)
    {
        var contents = new List<IContent>();
        
        if (obj is System.Text.Json.JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    var content = DeserializeContent(item);
                    if (content != null) contents.Add(content);
                }
            }
            else
            {
                var content = DeserializeContent(element);
                if (content != null) contents.Add(content);
            }
        }
        
        return contents;
    }

    private static object SerializeContent(IContent content)
    {
        return content switch
        {
            Text t => new { type = "text", value = t.Value },
            Reasoning r => new { type = "reasoning", thought = r.Thought },
            ToolCall tc => new { type = "toolCall", id = tc.Id, name = tc.Name, arguments = tc.Arguments },
            ToolResult tr => new { type = "toolResult", callId = tr.CallId, result = SerializeContent(tr.Result!) },
            _ => new { type = "text", value = content.ForLlm() }
        };
    }

    private static IContent DeserializeContent(object? obj)
    {
        if (obj is not System.Text.Json.JsonElement element) return new Text("");

        using var doc = JsonDocument.Parse(element.GetRawText());
        var root = doc.RootElement;

        if (root.TryGetProperty("type", out var typeProp))
        {
            var type = typeProp.GetString();
            switch (type)
            {
                case "text":
                    if (root.TryGetProperty("value", out var text))
                        return new Text(text.GetString() ?? "");
                    break;
                case "toolCall":
                    var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                    var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                    JsonObject args = new();
                    if (root.TryGetProperty("arguments", out var argsProp))
                    {
                        try { args = JsonNode.Parse(argsProp.GetRawText())?.AsObject() ?? new JsonObject(); } catch { }
                    }
                    return new ToolCall(id, name, args);
                case "toolResult":
                    var callId = root.TryGetProperty("callId", out var callIdProp) ? callIdProp.GetString() ?? "" : "";
                    IContent? result = null;
                    if (root.TryGetProperty("result", out var resultProp))
                        result = DeserializeContent(resultProp);
                    return new ToolResult(callId, result);
                case "summary":
                    if (root.TryGetProperty("text", out var summaryText))
                        return new Text(summaryText.GetString() ?? "");
                    break;
            }
        }

        if (root.ValueKind == JsonValueKind.String)
            return new Text(root.GetString() ?? "");

        return new Text("");
    }

    private class MessageDto
    {
        public string? Role { get; set; }
        public string? Kind { get; set; }
        public object? Content { get; set; }
    }

    private static IContent? DeserializeContent(JsonElement element)
    {
        if (element.TryGetProperty("type", out var typeProp))
        {
            var type = typeProp.GetString();
            switch (type)
            {
                case "text":
                    if (element.TryGetProperty("value", out var text))
                        return new Text(text.GetString() ?? "");
                    break;
                case "reasoning":
                    if (element.TryGetProperty("thought", out var thought))
                        return new Reasoning(thought.GetString() ?? "");
                    break;
                case "toolCall":
                    var id = element.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                    var name = element.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                    JsonObject args = new();
                    if (element.TryGetProperty("arguments", out var argsProp))
                    {
                        try { args = JsonNode.Parse(argsProp.GetRawText())?.AsObject() ?? new JsonObject(); } catch { }
                    }
                    return new ToolCall(id, name, args);
                case "toolResult":
                    var callId = element.TryGetProperty("callId", out var callIdProp) ? callIdProp.GetString() ?? "" : "";
                    IContent? result = null;
                    if (element.TryGetProperty("result", out var resultProp))
                        result = DeserializeContent(resultProp);
                    return new ToolResult(callId, result);
                case "summary":
                    if (element.TryGetProperty("text", out var summaryText))
                        return new Text(summaryText.GetString() ?? "");
                    break;
            }
        }

        if (element.ValueKind == JsonValueKind.String)
            return new Text(element.GetString() ?? "");

        return new Text("");
    }
}
