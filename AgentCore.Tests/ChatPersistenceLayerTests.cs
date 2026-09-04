using AgentCore.Context;
using AgentCore.Layers.Chat;
using AgentCore.LLM.Chat;
using Xunit;

namespace AgentCore.Tests;

public class ChatPersistenceLayerTests
{
    private class InMemoryChatStore : IChatStore
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
        var store = new InMemoryChatStore();
        store.Storage["session-1"] =
        [
            new(Role.User, [new Text("Hello from previous session")]),
            new(Role.Assistant, [new Text("Welcome back!")])
        ];

        var innerContext = new MockMemoryProvider();
        var layer = new ChatPersistenceLayer(store, "session-1", autoRestore: true);
        layer.Attach(innerContext);

        var messages = await layer.GetMessagesAsync();

        Assert.Equal(2, messages.Count);
        Assert.Equal("Hello from previous session", messages[0].Contents[0].ToString());
        Assert.Equal("Welcome back!", messages[1].Contents[0].ToString());
    }

    [Fact]
    public async Task AddAsync_SavesSnapshotToStore()
    {
        var store = new InMemoryChatStore();
        var innerContext = new MockMemoryProvider();
        var layer = new ChatPersistenceLayer(store, "session-2", autoRestore: true);
        layer.Attach(innerContext);

        var userMessage = new Message(Role.User, [new Text("New question")]);
        await layer.AddAsync([userMessage]);

        Assert.True(store.Storage.ContainsKey("session-2"));
        Assert.Single(store.Storage["session-2"]);
        Assert.Equal("New question", store.Storage["session-2"][0].Contents[0].ToString());
    }

    [Fact]
    public async Task AutoRestore_Disabled_DoesNotLoadExistingMessages()
    {
        var store = new InMemoryChatStore();
        store.Storage["session-3"] =
        [
            new(Role.User, [new Text("Previous message")])
        ];

        var innerContext = new MockMemoryProvider();
        var layer = new ChatPersistenceLayer(store, "session-3", autoRestore: false);
        layer.Attach(innerContext);

        var messages = await layer.GetMessagesAsync();
        Assert.Empty(messages);

        await layer.AddAsync([new Message(Role.User, [new Text("Fresh message")])]);
        Assert.Single(store.Storage["session-3"]);
        Assert.Equal("Fresh message", store.Storage["session-3"][0].Contents[0].ToString());
    }

    [Fact]
    public async Task RestoreAsync_RestoresWorkingContext_FromLatestCompactedSummary()
    {
        var store = new InMemoryChatStore();
        // Session history with multiple compactions
        store.Storage["session-compacted"] =
        [
            new(Role.System, [new Text("System instruction")]),
            new(Role.User, [new Text("First message")]),
            new(Role.Assistant, [new Text("First answer")]),
            new(Role.User, [new CompactedSummary("Summary 1")]),
            new(Role.User, [new Text("Second message")]),
            new(Role.Assistant, [new Text("Second answer")]),
            new(Role.User, [new CompactedSummary("Latest Summary 2")]),
            new(Role.User, [new Text("Third message")]),
            new(Role.Assistant, [new Text("Third answer")])
        ];

        var innerContext = new MockMemoryProvider();
        var layer = new ChatPersistenceLayer(store, "session-compacted", autoRestore: true);
        layer.Attach(innerContext);

        var workingContext = await layer.GetMessagesAsync();

        // Should reconstruct: System + Latest Summary 2 + Third message + Third answer
        Assert.Equal(4, workingContext.Count);
        Assert.Equal(Role.System, workingContext[0].Role);
        Assert.IsType<CompactedSummary>(workingContext[1].Contents[0]);
        Assert.Equal("Latest Summary 2", workingContext[1].Contents[0].ToString());
        Assert.Equal("Third message", workingContext[2].Contents[0].ToString());
        Assert.Equal("Third answer", workingContext[3].Contents[0].ToString());
    }

    [Fact]
    public void Constructor_InvalidArguments_ThrowsException()
    {
        var store = new InMemoryChatStore();

        Assert.Throws<ArgumentNullException>(() => new ChatPersistenceLayer(null!, "session"));
        Assert.Throws<ArgumentException>(() => new ChatPersistenceLayer(store, ""));
        Assert.Throws<ArgumentException>(() => new ChatPersistenceLayer(store, "   "));
    }

    [Fact]
    public void BuilderExtension_RegistersLayerProperly()
    {
        var store = new InMemoryChatStore();
        var mockLLM = new MockLLMProvider();

        var agent = new Agent.Builder()
            .WithLLM(_ => mockLLM)
            .AddChatPersistence(store, "session-builder-test")
            .Build();

        Assert.NotNull(agent);
    }
}
