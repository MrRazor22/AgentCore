with open("research/runs/evolution/agentcore/EvolutionTests.cs", "r", encoding="utf-8") as f:
    text = f.read()

# Fix Phase 2: assert on all deltas joined
old_p2 = """        Assert.True(deltas.Count >= 1);
        var text = deltas.LastOrDefault() ?? "";
        Assert.Contains("Streaming", text);"""

new_p2 = """        Assert.True(deltas.Count >= 1);
        var text = string.Concat(deltas);
        Assert.Contains("Streaming", text);"""

text = text.replace(old_p2, new_p2)

# Fix Phase 5: Case B should execute tool via approvalLayer and verify success
old_p5_b = """        // Case B: Approved
        approved = true;
        var llm2 = new MockEvolutionLLM((m, t, c) =>
        {
            if (c == 1) return YieldToolCall("c2", "execute_command", "{\\"command\\":\\"echo safe\\"}");
            return YieldText("Done");
        });
        var agent2 = new Agent(llm2, approvalLayer, new ChatContext());
        var resultsApproved = new List<IContentEvent>();
        await foreach (var evt in agent2.InvokeStreamingAsync([new Text("Execute safe cmd")]))
        {
            resultsApproved.Add(evt);
        }
        var textApproved = string.Join("", resultsApproved.OfType<Text>().Select(t => t.Value));
        Assert.Contains("safe", textApproved);"""

new_p5_b = """        // Case B: Approved
        approved = true;
        var resultsApproved = new List<IMessageEvent>();
        await foreach (var evt in approvalLayer.ExecuteAsync([new ToolCall("c2", "execute_command", "{\\"command\\":\\"echo safe\\"}")] ))
        {
            resultsApproved.Add(evt);
        }
        var deltaApproved = resultsApproved.OfType<MessageDelta>().FirstOrDefault(d => d.Content is ToolResult);
        Assert.NotNull(deltaApproved);
        var trApproved = Assert.IsType<ToolResult>(deltaApproved.Content);
        Assert.False(trApproved.IsError);
        Assert.Contains("safe", trApproved.Contents.OfType<Text>().First().Value);"""

text = text.replace(old_p5_b, new_p5_b)

with open("research/runs/evolution/agentcore/EvolutionTests.cs", "w", encoding="utf-8") as f:
    f.write(text)

print("Updated Phase 2 and Phase 5 in EvolutionTests.cs")
