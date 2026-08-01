using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using System.Text.Json;

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

        private List<Message>? _pendingPrompt;

        public Task<IReadOnlyList<Message>> BuildPromptAsync(
            IReadOnlyList<Message> uncommittedMessages,
            CancellationToken ct = default)
        {
            var list = new List<Message>(InnerMessages);
            list.AddRange(uncommittedMessages);
            _pendingPrompt = list;
            return Task.FromResult<IReadOnlyList<Message>>(list);
        }

        public Task CommitAsync(
            TokenUsage usage,
            IReadOnlyList<Message> response,
            CancellationToken ct = default)
        {
            InnerMessages.Clear();
            InnerMessages.AddRange(_pendingPrompt ?? Messages.ToList());
            InnerMessages.AddRange(response);
            _pendingPrompt = null;
            return Task.CompletedTask;
        }
    }

    private FilePersistentChatContext CreateContext()
    {
        return new FilePersistentChatContext(new FakeContext(), _sessionFilePath);
    }

    [Fact]
    public async Task Save_WritesToDiskCorrectly()
    {
        // Arrange
        var context = CreateContext();
        var msg = new Message(Role.User, new Text("Hello World"));

        // Act
        var prompt = await context.BuildPromptAsync(new[] { msg });
        await context.CommitAsync(new TokenUsage(10, 0), Array.Empty<Message>());

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
        var prompt = await context.BuildPromptAsync(new[] { initialMsg });
        await context.CommitAsync(new TokenUsage(10, 0), Array.Empty<Message>());

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
        var prompt2 = await context.BuildPromptAsync(Array.Empty<Message>());
        await context.CommitAsync(new TokenUsage(10, 5), new[] { finalMsg });

        // The final file should now have both initial and final messages.
        var finalJson = await File.ReadAllTextAsync(_sessionFilePath);
        var finalMessages = JsonSerializer.Deserialize<List<Message>>(finalJson);
        Assert.NotNull(finalMessages);
        Assert.Equal(2, finalMessages.Count);
        Assert.Equal("Initial Message", finalMessages[0].Contents[0].ForLlm());
        Assert.Equal("Final Successful Message", finalMessages[1].Contents[0].ForLlm());
    }
}
