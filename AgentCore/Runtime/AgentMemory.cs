using AgentCore.Conversation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCore.Runtime;

public interface IAgentMemory
{
    Task<Chat> RecallAsync(string sessionId);
    Task UpdateAsync(string sessionId, Chat chat);
    Task ClearAsync(string sessionId);
}

public sealed class InMemoryMemory : IAgentMemory
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Chat> _store = new();

    public Task<Chat> RecallAsync(string sessionId)
    {
        if (_store.TryGetValue(sessionId, out var chat))
        {
            var cloned = new Chat();
            foreach (var ex in chat.Turns)
                cloned.Add(ex);
            return Task.FromResult(cloned);
        }
        return Task.FromResult(new Chat());
    }

    public Task UpdateAsync(string sessionId, Chat chat)
    {
        var cloned = new Chat();
        foreach (var ex in chat.Turns)
            cloned.Add(ex);
        _store[sessionId] = cloned;
        return Task.CompletedTask;
    }

    public Task ClearAsync(string sessionId)
    {
        _store.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}

public sealed class FileMemoryOptions
{
    public string? PersistDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentCore");
}

public sealed class FileMemory(IOptions<FileMemoryOptions>? options) : IAgentMemory
{
    private readonly FileMemoryOptions _options = options?.Value ?? new FileMemoryOptions();
    private readonly SemaphoreSlim[] _sessionLocks = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public FileMemory() : this(null) { }

    private SemaphoreSlim GetLock(string sessionId)
        => _sessionLocks[Math.Abs(sessionId.GetHashCode()) % _sessionLocks.Length];

    public async Task<Chat> RecallAsync(string sessionId)
    {
        if (_options.PersistDir == null) return new Chat();

        var file = Path.Combine(_options.PersistDir, sessionId + ".json");
        if (!File.Exists(file)) return new Chat();

        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
            return FromJson(json);
        }
        catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
        {
            return new Chat();
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public async Task UpdateAsync(string sessionId, Chat chat)
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

    private static string ToJson(Chat chat)
    {
        var exchanges = chat.Turns.Select(e => new ExchangeDto
        {
            User = new MessageDto { Role = e.User.Role.ToString(), Content = SerializeContent(e.User.Content) },
            AssistantReply = e.AssistantReply != null ? new MessageDto
            {
                Role = e.AssistantReply.Role.ToString(),
                Content = SerializeContent(e.AssistantReply.Content)
            } : null,
            ToolSteps = e.ToolSteps.Select(t => new ToolStepDto
            {
                Call = new MessageDto { Role = t.Call.Role.ToString(), Content = SerializeContent(t.Call.Content) },
                Result = new MessageDto { Role = t.Result.Role.ToString(), Content = SerializeContent(t.Result.Content) }
            }).ToList()
        }).ToList();

        return JsonSerializer.Serialize(exchanges, new JsonSerializerOptions { WriteIndented = true });
    }

    private static Chat FromJson(string json)
    {
        var chat = new Chat();
        try
        {
            var exchanges = JsonSerializer.Deserialize<List<ExchangeDto>>(json);
            if (exchanges == null) return chat;

            foreach (var dto in exchanges)
            {
                var user = new Message(Enum.Parse<Role>(dto.User.Role), DeserializeContent(dto.User.Content));
                var toolSteps = dto.ToolSteps?.Select(t => (
                    new Message(Enum.Parse<Role>(t.Call.Role), DeserializeContent(t.Call.Content)),
                    new Message(Enum.Parse<Role>(t.Result.Role), DeserializeContent(t.Result.Content))
                )).ToList();
                Message? assistantReply = null;
                if (dto.AssistantReply != null)
                {
                    assistantReply = new Message(Enum.Parse<Role>(dto.AssistantReply.Role), DeserializeContent(dto.AssistantReply.Content));
                }
                chat.Add(new Turn(user, toolSteps, assistantReply));
            }
        }
        catch { }
        return chat;
    }

    private static object SerializeContent(IContent content)
    {
        return content switch
        {
            Text t => new { type = "text", value = t.Value },
            ToolCall tc => new { type = "toolCall", id = tc.Id, name = tc.Name, arguments = tc.Arguments },
            ToolResult tr => new { type = "toolResult", callId = tr.CallId, result = SerializeContent(tr.Result!) },
            Summary s => new { type = "summary", text = s.Text },
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
                        return new Summary(summaryText.GetString() ?? "");
                    break;
            }
        }

        if (root.ValueKind == JsonValueKind.String)
            return new Text(root.GetString() ?? "");

        return new Text("");
    }

    private class ExchangeDto
    {
        public MessageDto? User { get; set; }
        public MessageDto? AssistantReply { get; set; }
        public List<ToolStepDto>? ToolSteps { get; set; }
    }

    private class MessageDto
    {
        public string? Role { get; set; }
        public object? Content { get; set; }
    }

    private class ToolStepDto
    {
        public MessageDto? Call { get; set; }
        public MessageDto? Result { get; set; }
    }
}
