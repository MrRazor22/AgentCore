with open("research/runs/evolution/agentcore/EvolutionTests.cs", "r", encoding="utf-8") as f:
    text = f.read()

text = text.replace("Assert.NotEmpty(resultsApproved);\n\n        [Fact]", "Assert.NotEmpty(resultsApproved);\n    }\n\n    [Fact]")

with open("research/runs/evolution/agentcore/EvolutionTests.cs", "w", encoding="utf-8") as f:
    f.write(text)

print("Fixed closing brace on Phase 5")
