using AgentCore.Context;
using AgentCore.Layers.Context;
using AgentCore.LLM.Chat;
using Xunit;

namespace AgentCore.Tests;

public class ContextPersistenceLayerTests
{
    private class InMemoryContextStore : IContextStore
    {
        public Dictionary<string, List<Message>> Storage { get; } = new();

        public Task<IReadOnlyList<Message>?> LoadAsync(string sessionId, CancellationToken ct = default)
        {
            if (Storage.TryGetValue(sessionId, out var messages))
            {
                return Task.FromResult<IReadOnlyList<Message>?>(messages.ToList());
            }
            return Task.FromResult<IReadOnlyList<Message>?>(null);
        }

        public Task SaveAsync(string sessionId, IReadOnlyList<Message> messages, CancellationToken ct = default)
        {
            Storage[sessionId] = messages.ToList();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task GetMessagesAsync_RestoresExistingMessagesFromStore()
    {
        var store = new InMemoryContextStore();
        store.Storage["session-1"] = new List<Message>
        {
            new(Role.User, new Text("Hello from previous session")),
            new(Role.Assistant, new Text("Welcome back!"))
        };

        var innerContext = new MockMemoryProvider();
        var layer = new ContextPersistenceLayer(store, "session-1", autoRestore: true);
        layer.Attach(innerContext);

        var messages = await layer.GetMessagesAsync();

        Assert.Equal(2, messages.Count);
        Assert.Equal("Hello from previous session", messages[0].Contents[0].ToString());
        Assert.Equal("Welcome back!", messages[1].Contents[0].ToString());
    }

    [Fact]
    public async Task AddAsync_SavesSnapshotToStore()
    {
        var store = new InMemoryContextStore();
        var innerContext = new MockMemoryProvider();
        var layer = new ContextPersistenceLayer(store, "session-2", autoRestore: true);
        layer.Attach(innerContext);

        var userMessage = new Message(Role.User, new Text("New question"));
        await layer.AddAsync([userMessage]);

        Assert.True(store.Storage.ContainsKey("session-2"));
        Assert.Single(store.Storage["session-2"]);
        Assert.Equal("New question", store.Storage["session-2"][0].Contents[0].ToString());
    }

    [Fact]
    public async Task AutoRestore_Disabled_DoesNotLoadExistingMessages()
    {
        var store = new InMemoryContextStore();
        store.Storage["session-3"] = new List<Message>
        {
            new(Role.User, new Text("Previous message"))
        };

        var innerContext = new MockMemoryProvider();
        var layer = new ContextPersistenceLayer(store, "session-3", autoRestore: false);
        layer.Attach(innerContext);

        var messages = await layer.GetMessagesAsync();
        Assert.Empty(messages);

        // Adding a message still saves only the new message
        await layer.AddAsync([new Message(Role.User, new Text("Fresh message"))]);
        Assert.Single(store.Storage["session-3"]);
        Assert.Equal("Fresh message", store.Storage["session-3"][0].Contents[0].ToString());
    }

    [Fact]
    public void Constructor_InvalidArguments_ThrowsException()
    {
        var store = new InMemoryContextStore();

        Assert.Throws<ArgumentNullException>(() => new ContextPersistenceLayer(null!, "session"));
        Assert.Throws<ArgumentException>(() => new ContextPersistenceLayer(store, ""));
        Assert.Throws<ArgumentException>(() => new ContextPersistenceLayer(store, "   "));
    }

    [Fact]
    public void BuilderExtension_RegistersLayerProperly()
    {
        var store = new InMemoryContextStore();
        var mockLLM = new MockLLMProvider();

        var agent = new Agent.Builder()
            .WithLLM(_ => mockLLM)
            .AddContextPersistence(store, "session-builder-test")
            .Build();

        Assert.NotNull(agent);
    }
}
