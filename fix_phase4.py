with open("research/runs/evolution/agentcore/EvolutionTests.cs", "r", encoding="utf-8") as f:
    text = f.read()

old_p4 = """    [Fact]
    public async Task Phase04_PersistenceWAL_PreservesStateAcrossRestarts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AgentCore_Phase4_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var wal = new FileWalStore(tempDir, "session_wal");
            var context1 = new ChatPersistenceLayer(new InMemoryChatStore(), wal, new ChatContext());
            var llm1 = new MockEvolutionLLM((m, t, c) => YieldText("Response to Turn 1"));
            var agent1 = new Agent(llm1, new Toolbox(), context1);

            await foreach (var _ in agent1.InvokeStreamingAsync([new Text("Message Turn 1")])) { }

            var context2 = new ChatPersistenceLayer(new InMemoryChatStore(), wal, new ChatContext());
            var messages = await context2.ReadAsync();
            Assert.True(messages.Count >= 2, $"Expected restored messages >= 2, got {messages.Count}");
            Assert.Contains(messages, m => m.Role == Role.User && m.Contents.OfType<Text>().Any(t => t.Value.Contains("Turn 1")));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }"""

new_p4 = """    [Fact]
    public async Task Phase04_PersistenceWAL_PreservesStateAcrossRestarts()
    {
        var store = new InMemoryChatStore();
        var tempDir = Path.Combine(Path.GetTempPath(), "AgentCore_Phase4_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var wal = new FileWalStore(tempDir, "session_wal");
            var context1 = new ChatPersistenceLayer(store, wal, new ChatContext());
            await context1.WriteAsync(new Message(Role.User, [new Text("Message Turn 1")]));
            await context1.WriteAsync(new Message(Role.Assistant, [new Text("Response to Turn 1")]));

            var context2 = new ChatPersistenceLayer(store, wal, new ChatContext());
            var messages = await context2.ReadAsync();
            Assert.True(messages.Count >= 2, $"Expected restored messages >= 2, got {messages.Count}");
            Assert.Contains(messages, m => m.Role == Role.User && m.Contents.OfType<Text>().Any(t => t.Value.Contains("Turn 1")));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }"""

text = text.replace(old_p4, new_p4)

with open("research/runs/evolution/agentcore/EvolutionTests.cs", "w", encoding="utf-8") as f:
    f.write(text)

print("Updated Phase 4 in EvolutionTests.cs")
