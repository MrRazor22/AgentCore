using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgentCore.Context;
using AgentCore.Layers.Chat;
using AgentCore.LLM.Chat;
using Xunit;

namespace AgentCore.Tests;

public class SpilloverTruncationLayerTests
{
    private sealed class InMemoryContext : IContext
    {
        public List<Message> Messages { get; } = new();

        public Task<IReadOnlyList<Message>> GetMessagesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Message>>(Messages);

        public Task AddAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
        {
            Messages.AddRange(messages);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AddAsync_UnderBudget_DoesNotSpill()
    {
        var inner = new InMemoryContext();
        var sessionDir = Path.Combine(Path.GetTempPath(), "test_spill_" + Guid.NewGuid().ToString("N"));

        using var layer = new SpilloverTruncationLayer(maxTokens: 100, storageDir: sessionDir);
        layer.Attach(inner);

        var msg = new Message(Role.User, [new Text("Short text")]);
        await layer.AddAsync([msg]);

        var result = Assert.Single(inner.Messages);
        var textContent = Assert.IsType<Text>(Assert.Single(result.Contents));
        Assert.Equal("Short text", textContent.Value);
        Assert.False(Directory.Exists(sessionDir));
    }

    [Fact]
    public async Task AddAsync_OverBudget_SpillsTextToDiskAndAttachesNotice()
    {
        var inner = new InMemoryContext();
        var sessionDir = Path.Combine(Path.GetTempPath(), "test_spill_" + Guid.NewGuid().ToString("N"));

        using var layer = new SpilloverTruncationLayer(maxTokens: 50, storageDir: sessionDir);
        layer.Attach(inner);

        string bigText = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"Line {i}: Some long output content here..."));
        var msg = new Message(Role.User, [new Text(bigText)]);
        await layer.AddAsync([msg]);

        var result = Assert.Single(inner.Messages);
        var textContent = Assert.IsType<Text>(Assert.Single(result.Contents));
        Assert.Contains("Output truncated", textContent.Value);
        Assert.Contains("100 lines", textContent.Value);
        Assert.Contains(".log", textContent.Value);

        // Verify spill file was created with exact full content
        var files = Directory.GetFiles(sessionDir, "*.log");
        Assert.Single(files);
        Assert.Equal(bigText, File.ReadAllText(files[0]));
    }

    [Fact]
    public async Task AddAsync_CustomNoticeFormatter_AppliesNotice()
    {
        var inner = new InMemoryContext();
        var sessionDir = Path.Combine(Path.GetTempPath(), "test_spill_" + Guid.NewGuid().ToString("N"));

        using var layer = new SpilloverTruncationLayer(
            maxTokens: 50,
            storageDir: sessionDir,
            noticeFormatter: (path, lines) => $"\n<<<CUT {lines} LINES -> {path}>>>");
        layer.Attach(inner);

        string bigText = string.Join("\n", Enumerable.Range(1, 50).Select(i => $"Line {i} content"));
        await layer.AddAsync([new Message(Role.User, [new Text(bigText)])]);

        var result = Assert.Single(inner.Messages);
        var textContent = Assert.IsType<Text>(Assert.Single(result.Contents));
        Assert.Contains("<<<CUT 50 LINES ->", textContent.Value);
    }

    [Fact]
    public async Task Dispose_AutoDeleteOnDispose_RemovesSessionDirectory()
    {
        var inner = new InMemoryContext();
        var sessionDir = Path.Combine(Path.GetTempPath(), "test_spill_" + Guid.NewGuid().ToString("N"));

        var layer = new SpilloverTruncationLayer(maxTokens: 20, storageDir: sessionDir, autoDeleteOnDispose: true);
        layer.Attach(inner);

        string bigText = string.Join("\n", Enumerable.Range(1, 50).Select(i => $"Line {i} content"));
        await layer.AddAsync([new Message(Role.User, [new Text(bigText)])]);

        Assert.True(Directory.Exists(sessionDir));

        layer.Dispose();

        Assert.False(Directory.Exists(sessionDir));
    }
}
