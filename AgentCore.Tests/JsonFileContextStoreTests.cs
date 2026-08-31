using AgentCore.Layers.Context;
using AgentCore.LLM.Chat;
using Xunit;

namespace AgentCore.Tests;

public class JsonFileContextStoreTests : IDisposable
{
    private readonly string _tempDir;

    public JsonFileContextStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "agentcore_tests_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task SaveAndLoad_PersistsAndRestoresMessages()
    {
        var store = new JsonFileContextStore(_tempDir);
        var messages = new List<Message>
        {
            new(Role.User, new Text("Hello")),
            new(Role.Assistant, new Text("Hi there!"))
        };

        await store.SaveAsync("session-1", messages);
        var loaded = await store.LoadAsync("session-1");

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Count);
        Assert.Equal(Role.User, loaded[0].Role);
        Assert.Equal("Hello", loaded[0].Contents[0].ToString());
        Assert.Equal(Role.Assistant, loaded[1].Role);
        Assert.Equal("Hi there!", loaded[1].Contents[0].ToString());
    }

    [Fact]
    public async Task Load_NonExistentSession_ReturnsNull()
    {
        var store = new JsonFileContextStore(_tempDir);
        var loaded = await store.LoadAsync("non-existent");
        Assert.Null(loaded);
    }

    [Fact]
    public void Constructor_InvalidDirectory_Throws()
    {
        Assert.Throws<ArgumentException>(() => new JsonFileContextStore(""));
        Assert.Throws<ArgumentException>(() => new JsonFileContextStore("   "));
    }
}
