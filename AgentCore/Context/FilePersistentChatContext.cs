using AgentCore.LLM;
using AgentCore.LLM.Chat;
using System.Text.Json;

namespace AgentCore.Context;

public class FilePersistentChatContext : ContextLayer
{
    private readonly string _filePath;
    private IReadOnlyList<Message>? _lastPreparedPrompt;

    public FilePersistentChatContext(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public override async Task<IReadOnlyList<Message>> BuildPromptAsync(
        IReadOnlyList<Message> uncommittedMessages,
        CancellationToken ct = default)
    {
        var prepared = await base.BuildPromptAsync(uncommittedMessages, ct).ConfigureAwait(false);
        _lastPreparedPrompt = prepared;
        return prepared;
    }

    public override async Task CommitAsync(
        TokenUsage usage,
        IReadOnlyList<Message> response,
        CancellationToken ct = default)
    {
        await base.CommitAsync(usage, response, ct).ConfigureAwait(false);

        var messages = new List<Message>(_lastPreparedPrompt ?? Array.Empty<Message>());
        messages.AddRange(response);

        await SaveToDiskAsync(messages, ct).ConfigureAwait(false);
        _lastPreparedPrompt = null;
    }

    private async Task SaveToDiskAsync(List<Message> messages, CancellationToken ct)
    {
        var tempPath = _filePath + ".tmp";
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(messages, new JsonSerializerOptions { WriteIndented = true });

        // Write to temporary file and swap
        await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);

        if (File.Exists(_filePath))
        {
            File.Replace(tempPath, _filePath, null);
        }
        else
        {
            File.Move(tempPath, _filePath);
        }
    }
}
