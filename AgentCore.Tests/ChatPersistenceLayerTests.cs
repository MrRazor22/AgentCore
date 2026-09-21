using AgentCore.Context;
using AgentCore.Layers.Context;
using AgentCore.Layers.Context.Store;
using AgentCore.LLM.Chat;
using Xunit;

namespace AgentCore.Tests;

public class ChatPersistenceLayerTests
{
    private class InMemoryChatStore : IChatStore
    {
        public List<Message> Storage { get; set; } = [];

        public Task<IReadOnlyList<Message>?> LoadAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Message>?>(Storage.Count > 0 ? Storage.ToList() : null);

        public Task AppendAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
        {
            Storage.AddRange(messages);
            return Task.CompletedTask;
        }
    }

    private class InMemoryWalStore : IWalStore
    {
        public List<IMessageEvent> Storage { get; set; } = [];
        public bool Cleared { get; private set; }

        public Task AppendAsync(IMessageEvent evt, CancellationToken ct = default)
        {
            Storage.Add(evt);
            Cleared = false;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken ct = default)
        {
            Cleared = true;
            Storage.Clear();
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<IMessageEvent> RecoverAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var evt in Storage)
            {
                yield return evt;
            }
        }
    }

    [Fact]
    public async Task GetMessagesAsync_RestoresExistingMessagesFromStore()
    {
        var store = new InMemoryChatStore();
        store.Storage =
        [
            new(Role.User, [new Text("Hello from previous session")]),
            new(Role.Assistant, [new Text("Welcome back!")])
        ];

        var innerContext = new MockMemoryProvider();
        var layer = new ChatPersistenceLayer(store);
        layer.Attach(innerContext);

        var messages = await layer.PrepareAsync();

        Assert.Equal(2, messages.Count);
        Assert.Equal("Hello from previous session", messages[0].Contents[0].ToString());
        Assert.Equal("Welcome back!", messages[1].Contents[0].ToString());
    }

    [Fact]
    public async Task AddAsync_AppendsMessagesToStore()
    {
        var store = new InMemoryChatStore();
        var innerContext = new MockMemoryProvider();
        var layer = new ChatPersistenceLayer(store);
        layer.Attach(innerContext);

        var userMessage = new Message(Role.User, [new Text("New question")]);
        await layer.PrepareAsync([userMessage]);

        Assert.Single(store.Storage);
        Assert.Equal("New question", store.Storage[0].Contents[0].ToString());
    }

    [Fact]
    public async Task RestoreAsync_RestoresWorkingContext_FromLatestSummary()
    {
        var store = new InMemoryChatStore();
        // Session history with multiple compactions
        store.Storage =
        [
            new(Role.System, [new Text("System instruction")]),
            new(Role.User, [new Text("First message")]),
            new(Role.Assistant, [new Text("First answer")]),
            new(Role.User, [new Text("Summary 1")], metadata: [new Summary(3)]),
            new(Role.User, [new Text("Second message")]),
            new(Role.Assistant, [new Text("Second answer")]),
            new(Role.User, [new Text("Latest Summary 2")], metadata: [new Summary(6)]),
            new(Role.User, [new Text("Third message")]),
            new(Role.Assistant, [new Text("Third answer")])
        ];

        var innerContext = new MockMemoryProvider();
        var layer = new ChatPersistenceLayer(store);
        layer.Attach(innerContext);

        var workingContext = await layer.PrepareAsync();

        // Should reconstruct: System + Latest Summary 2 + Third message + Third answer
        Assert.Equal(4, workingContext.Count);
        Assert.Equal(Role.System, workingContext[0].Role);
        Assert.NotNull(workingContext[1].Get<Summary>());
        Assert.Equal("Latest Summary 2", workingContext[1].Contents[0].ToString());
        Assert.Equal("Third message", workingContext[2].Contents[0].ToString());
        Assert.Equal("Third answer", workingContext[3].Contents[0].ToString());
    }

    [Fact]
    public void Constructor_InvalidArguments_ThrowsException()
    {
        Assert.Throws<ArgumentNullException>(() => new ChatPersistenceLayer(null!));
    }

    [Fact]
    public void BuilderExtension_RegistersLayerProperly()
    {
        var store = new InMemoryChatStore();
        var mockLLM = new MockLLMProvider();

        var agent = Agent.Create()
            .WithLLM(_ => mockLLM)
            .AddChatPersistence(store)
            .Build();

        Assert.NotNull(agent);
    }

    [Fact]
    public async Task Cancellation_WithoutWal_PersistsPartialMessageAndFiltersPartialToolCalls()
    {
        var store = new InMemoryChatStore();
        var innerContext = new ChatContext();
        var layer = new ChatPersistenceLayer(store, walStore: null);
        layer.Attach(innerContext);

        await layer.PrepareAsync([new Message(Role.User, [new Text("Tell me a story")])]);

        using var cts = new CancellationTokenSource();

        async IAsyncEnumerable<IMessageEvent> GenerateStream([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new MessageStart(Role.Assistant, "msg-1");
            yield return new MessageDelta("msg-1", Content: new TextStart(0));
            yield return new MessageDelta("msg-1", Content: new TextDelta(0, "Once upon a time"));
            yield return new MessageDelta("msg-1", Content: new ToolCallStart(1, "call-1", "Search"));
            yield return new MessageDelta("msg-1", Content: new ToolCallDelta(1, "{\"query\":"));
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            yield break;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var evt in layer.WriteAsync(GenerateStream(cts.Token), cts.Token))
            {
            }
        });

        Assert.Equal(2, store.Storage.Count);
        var stored = store.Storage;
        Assert.Equal(Role.User, stored[0].Role);
        Assert.Equal(Role.Assistant, stored[1].Role);
        Assert.Single(stored[1].Contents);
        Assert.IsType<Text>(stored[1].Contents[0]);
        Assert.Equal("Once upon a time", stored[1].Contents[0].ToString());
    }

    [Fact]
    public async Task NormalCompletion_WithWal_PersistsSingleMessageAndClearsWal()
    {
        var store = new InMemoryChatStore();
        var walStore = new InMemoryWalStore();
        var innerContext = new ChatContext();
        var layer = new ChatPersistenceLayer(store, walStore: walStore);
        layer.Attach(innerContext);

        await layer.PrepareAsync([new Message(Role.User, [new Text("Hi")])]);

        async IAsyncEnumerable<IMessageEvent> GenerateStream([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new MessageStart(Role.Assistant, "msg-2");
            yield return new MessageDelta("msg-2", Content: new ReasoningStart(0));
            yield return new MessageDelta("msg-2", Content: new ReasoningDelta(0, "Thinking..."));
            yield return new MessageDelta("msg-2", Content: new ReasoningEnd(0));
            yield return new MessageDelta("msg-2", Content: new TextStart(1));
            yield return new MessageDelta("msg-2", Content: new TextDelta(1, "Hello!"));
            yield return new MessageDelta("msg-2", Content: new TextEnd(1));
            yield return new MessageEnd("msg-2");
        }

        await foreach (var evt in layer.WriteAsync(GenerateStream()))
        {
        }

        Assert.Equal(2, store.Storage.Count);
        var stored = store.Storage;
        Assert.Equal(Role.User, stored[0].Role);
        Assert.Equal(Role.Assistant, stored[1].Role);
        Assert.Equal(2, stored[1].Contents.Count);
        Assert.True(walStore.Cleared);
    }

    [Fact]
    public async Task CrashRecovery_ReplaysUncommittedWal_OnRestore()
    {
        var store = new InMemoryChatStore();
        store.Storage = [new Message(Role.User, [new Text("Initial question")])];

        var walStore = new InMemoryWalStore();
        walStore.Storage =
        [
            new MessageStart(Role.Assistant, "msg-crash"),
            new MessageDelta("msg-crash", Content: new TextStart(0)),
            new MessageDelta("msg-crash", Content: new TextDelta(0, "Recovered before crash")),
            new MessageDelta("msg-crash", Content: new ToolCallStart(1, "call-crash", "UnfinishedTool")),
            new MessageDelta("msg-crash", Content: new ToolCallDelta(1, "{\"partial\":"))
            // Process killed! No TextEnd, No ToolCallEnd, No MessageEnd
        ];

        var innerContext = new ChatContext();
        var layer = new ChatPersistenceLayer(store, walStore: walStore);
        layer.Attach(innerContext);

        var restored = await layer.PrepareAsync();

        // Must have user message + recovered assistant message
        Assert.Equal(2, restored.Count);
        Assert.Equal(Role.User, restored[0].Role);
        Assert.Equal(Role.Assistant, restored[1].Role);
        // Incomplete tool call was stripped, text was preserved
        Assert.Single(restored[1].Contents);
        Assert.Equal("Recovered before crash", restored[1].Contents[0].ToString());
        Assert.True(walStore.Cleared);
    }

    [Fact]
    public async Task CrashRecovery_MultiMessageWal_RestoresAllMessages()
    {
        var store = new InMemoryChatStore();
        store.Storage = [new Message(Role.User, [new Text("Deploy server")])];

        var walStore = new InMemoryWalStore();
        walStore.Storage =
        [
            // Assistant message calling two tools
            new MessageStart(Role.Assistant, "msg-ast"),
            new MessageDelta("msg-ast", Content: new ToolCallStart(0, "call-1", "ChargeCard")),
            new MessageDelta("msg-ast", Content: new ToolCallDelta(0, "{}")),
            new MessageDelta("msg-ast", Content: new ToolCallEnd(0)),
            new MessageDelta("msg-ast", Content: new ToolCallStart(1, "call-2", "ProvisionServer")),
            new MessageDelta("msg-ast", Content: new ToolCallDelta(1, "{}")),
            new MessageDelta("msg-ast", Content: new ToolCallEnd(1)),
            new MessageEnd("msg-ast"),

            // Completed tool result for call-1
            new MessageStart(Role.Tool, "call-1"),
            new MessageDelta("call-1", Content: new ToolResult("call-1", [new Text("Charged successfully")])),
            new MessageEnd("call-1")
        ];

        var innerContext = new ChatContext();
        var layer = new ChatPersistenceLayer(store, walStore: walStore);
        layer.Attach(innerContext);

        var restored = await layer.PrepareAsync();

        // Must have: User, Assistant (with 2 tools), Tool 1 result
        Assert.Equal(3, restored.Count);
        Assert.Equal(Role.User, restored[0].Role);
        Assert.Equal(Role.Assistant, restored[1].Role);
        Assert.Equal(2, restored[1].Contents.Count);
        Assert.Equal(Role.Tool, restored[2].Role);
        var tr = restored[2].Contents.OfType<ToolResult>().First();
        Assert.Equal("call-1", tr.ToolCallId);
        Assert.Equal("Charged successfully", tr.Contents[0].ToString());
        Assert.True(walStore.Cleared);
    }

    [Fact]
    public async Task AddChatPersistence_DirectoryPath_PersistsAndRestoresFromFileStore()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "agentcore_tests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileChatStore(tempDir, "session-file-test");
            var wal = new FileWalStore(tempDir, "session-file-test");
            var layer = new ChatPersistenceLayer(store, walStore: wal);
            layer.Attach(new ChatContext());

            await layer.PrepareAsync([new Message(Role.User, [new Text("Saved to disk")])]);

            // Create new layer pointing to same directory
            var newStore = new FileChatStore(tempDir, "session-file-test");
            var newWal = new FileWalStore(tempDir, "session-file-test");
            var newLayer = new ChatPersistenceLayer(newStore, walStore: newWal);
            newLayer.Attach(new ChatContext());

            var loaded = await newLayer.PrepareAsync();
            Assert.Single(loaded);
            Assert.Equal("Saved to disk", loaded[0].Contents[0].ToString());
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
