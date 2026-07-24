using AgentCore.LLM.Chat;
using System.Text.Json;

namespace AgentCore.Context;

public class FilePersistentChatContext : ContextLayer
{
    private readonly string _filePath;
    private readonly List<Message> _messages = new();

    public FilePersistentChatContext(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public override IReadOnlyList<Message> Messages => base.Messages;

    public override async Task AddAsync(Message message, CancellationToken ct = default)
    {
        await base.AddAsync(message, ct).ConfigureAwait(false);
        _messages.Add(message);
        await SaveToDiskAsync(ct).ConfigureAwait(false);
    }

    public override async Task ClearAsync(CancellationToken ct = default)
    {
        _messages.Clear();
        await SaveToDiskAsync(ct).ConfigureAwait(false);
        await base.ClearAsync(ct).ConfigureAwait(false);
    }

    public override async Task AddRangeAsync(IEnumerable<Message> messages, CancellationToken ct = default)
    {
        await base.AddRangeAsync(messages, ct).ConfigureAwait(false);
        if (messages != null)
        {
            _messages.AddRange(messages);
        }
        await SaveToDiskAsync(ct).ConfigureAwait(false);
    }

    private async Task SaveToDiskAsync(CancellationToken ct)
    {
        var tempPath = _filePath + ".tmp";
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(_messages, new JsonSerializerOptions { WriteIndented = true });

        // Write to the temporary file first
        await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);

        // Swap/replace the target file safely
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
