with open("research/runs/evolution/agentcore/EvolutionTests.cs", "r", encoding="utf-8") as f:
    lines = f.readlines()

new_lines = []
skip = False
for line in lines:
    if "var approvalLayer = new ToolApprovalLayer" in line:
        new_lines.append("        var approvalLayer = new EvolutionApprovalLayer(call => approved, toolbox);\n")
    elif "var guardrail = new InputGuardrailLayer" in line:
        new_lines.append("""        var guardrail = new EvolutionGuardrailLayer(text =>
        {
            if (Regex.IsMatch(text, @"(DROP|IGNORE INSTRUCTIONS)", RegexOptions.IgnoreCase))
            {
                return (false, "Security Violation: Prohibited pattern detected.");
            }
            return (true, string.Empty);
        }, new MockEvolutionLLM((m, t, c) => YieldText("Safe reply")));
""")
        skip = True
    elif skip and "var agent = new Agent(guardrail" in line:
        skip = False
        new_lines.append(line)
    elif not skip:
        new_lines.append(line)

with open("research/runs/evolution/agentcore/EvolutionTests.cs", "w", encoding="utf-8") as f:
    f.writelines(new_lines)
print("Applied fix successfully")
