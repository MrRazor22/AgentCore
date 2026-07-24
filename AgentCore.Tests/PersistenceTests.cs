using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using AgentCore.Context;
using AgentCore.LLM.Chat;

namespace AgentCore.Tests;

public class PersistenceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _sessionFilePath;

    public PersistenceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
        _sessionFilePath = Path.Combine(_tempDirectory, "session.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    private class FakeContext : IContext
    {
        public List<Message> InnerMessages { get; } = new();
        public IReadOnlyList<Message> Messages => InnerMessages;

        public Task AddAsync(Message message, CancellationToken ct = default)
        {
            InnerMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<Message> messages, CancellationToken ct = default)
        {
            InnerMessages.AddRange(messages);
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken ct = default)
        {
            InnerMessages.Clear();
            return Task.CompletedTask;
        }
    }

    private FilePersistentChatContext CreateContext()
    {
        var context = new FilePersistentChatContext(_sessionFilePath);
        context.Attach(new FakeContext());
        return context;
    }

    [Fact]
    public async Task Save_WritesToDiskCorrectly()
    {
        // Arrange
        var context = CreateContext();
        var msg = new Message(Role.User, new Text("Hello World"));

        // Act
        await context.AddAsync(msg);

        // Assert
        Assert.True(File.Exists(_sessionFilePath));
        var json = await File.ReadAllTextAsync(_sessionFilePath);
        var loadedMessages = JsonSerializer.Deserialize<List<Message>>(json);
        Assert.NotNull(loadedMessages);
        Assert.Single(loadedMessages);
        Assert.Equal("Hello World", loadedMessages[0].Contents[0].ForLlm());
    }

    [Fact]
    public async Task Save_WhenCrashBeforeReplace_KeepsOriginalFileIntact()
    {
        // Arrange
        var context = CreateContext();
        var initialMsg = new Message(Role.User, new Text("Initial Message"));
        await context.AddAsync(initialMsg);

        // Act - Simulate a crash during the next save right before the replace step
        // We write to the .tmp file manually to simulate a partial save where the app crashed.
        var tempPath = _sessionFilePath + ".tmp";
        var badMessages = new List<Message>
        {
            initialMsg,
            new Message(Role.Assistant, new Text("New Incomplete Response"))
        };
        var badJson = JsonSerializer.Serialize(badMessages, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(tempPath, badJson);

        // Assert
        // The original session file must still contain only the initial message and remain completely intact.
        Assert.True(File.Exists(_sessionFilePath));
        var originalJson = await File.ReadAllTextAsync(_sessionFilePath);
        var loadedOriginal = JsonSerializer.Deserialize<List<Message>>(originalJson);
        Assert.NotNull(loadedOriginal);
        Assert.Single(loadedOriginal);
        Assert.Equal("Initial Message", loadedOriginal[0].Contents[0].ForLlm());

        // Now, if we write again, the temp file is overwritten and replaced successfully.
        var finalMsg = new Message(Role.Assistant, new Text("Final Successful Message"));
        await context.AddAsync(finalMsg);

        // The final file should now have both initial and final messages.
        var finalJson = await File.ReadAllTextAsync(_sessionFilePath);
        var finalMessages = JsonSerializer.Deserialize<List<Message>>(finalJson);
        Assert.NotNull(finalMessages);
        Assert.Equal(2, finalMessages.Count);
        Assert.Equal("Initial Message", finalMessages[0].Contents[0].ForLlm());
        Assert.Equal("Final Successful Message", finalMessages[1].Contents[0].ForLlm());
    }
}
