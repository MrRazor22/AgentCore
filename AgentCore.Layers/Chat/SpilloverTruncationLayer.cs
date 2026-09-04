using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.Context;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Chat;

/// <summary>
/// Decorator context layer that spills oversized text content to a session-scoped temporary directory
/// and injects a customizable truncation notice referencing the saved file path.
/// </summary>
public sealed class SpilloverTruncationLayer : ContextLayer, IDisposable
{
    private readonly int _maxTokens;
    private readonly string _storageDir;
    private readonly bool _autoDeleteOnDispose;
    private readonly Func<string, int, string>? _noticeFormatter;

    public SpilloverTruncationLayer(
        int maxTokens = 10_000,
        string? sessionId = null,
        string? storageDir = null,
        bool autoDeleteOnDispose = true,
        Func<string, int, string>? noticeFormatter = null)
    {
        if (maxTokens <= 0) throw new ArgumentOutOfRangeException(nameof(maxTokens), "maxTokens must be positive.");

        _maxTokens = maxTokens;
        _autoDeleteOnDispose = autoDeleteOnDispose;
        _noticeFormatter = noticeFormatter;

        string session = !string.IsNullOrWhiteSpace(sessionId) ? sessionId : Guid.NewGuid().ToString("N");
        _storageDir = storageDir ?? Path.Combine(Path.GetTempPath(), "agentcore", "spillover", session);
    }

    public string StorageDirectory => _storageDir;

    public override Task AddAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var processed = messages.Select(msg =>
        {
            if (msg.Role == Role.System) return msg;

            var newContents = msg.Contents.Select(ProcessContent).ToList();
            return new Message(msg.Role, newContents, msg.Metadata);
        }).ToList();

        return base.AddAsync(processed, ct);
    }

    private IContent ProcessContent(IContent content) => content switch
    {
        ToolResult tr => ProcessToolResult(tr),
        Text t when t.EstimateTokens() > _maxTokens => SpillText(t),
        _ => content
    };

    private IContent ProcessToolResult(ToolResult tr)
    {
        if (tr.EstimateTokens() <= _maxTokens) return tr;

        var processedContents = tr.Contents.Select(ProcessContent).ToList();
        return new ToolResult(tr.CallId, processedContents);
    }

    private IContent SpillText(Text text)
    {
        string filePath = SaveSpillFile(text.Value);
        int totalLines = text.Value.AsSpan().Count('\n') + 1;

        string notice = _noticeFormatter != null
            ? _noticeFormatter(filePath, totalLines)
            : $"\n... [Output truncated ({totalLines} lines). Full output saved to: {filePath}]";

        return text.Truncate(_maxTokens, notice);
    }

    private string SaveSpillFile(string content)
    {
        Directory.CreateDirectory(_storageDir);
        string fileName = $"spill_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}.log";
        string fullPath = Path.Combine(_storageDir, fileName);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public void Dispose()
    {
        if (_autoDeleteOnDispose && Directory.Exists(_storageDir))
        {
            try { Directory.Delete(_storageDir, recursive: true); }
            catch { /* Best effort on disposal */ }
        }
    }
}
