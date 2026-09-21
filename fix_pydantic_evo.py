with open("research/runs/evolution/pydantic_ai/test_pydantic_ai_evolution.py", "r", encoding="utf-8") as f:
    text = f.read()

# Replace sys.path insertion at top
if "sys.path.insert" not in text:
    text = "import sys\nfrom pathlib import Path\nsys.path.insert(0, str(Path(__file__).resolve().parent))\n" + text

# Replace .data with .output
text = text.replace(".data", ".output")

# Fix Phase 6: make subagent_calculator async def
old_p6 = """    def test_phase06_multi_agent_tool(self):
        \"\"\"Phase 6: Multi-Agent Tool\"\"\"
        child_agent = Agent(TestModel())
        parent_agent = Agent(TestModel())

        @parent_agent.tool
        def subagent_calculator(ctx, query: str) -> str:
            child_res = child_agent.run_sync(query)
            return f"Child response: 42"

        result = parent_agent.run_sync("Calculate math")
        self.assertIsNotNone(result.output)"""

new_p6 = """    def test_phase06_multi_agent_tool(self):
        \"\"\"Phase 6: Multi-Agent Tool\"\"\"
        child_agent = Agent(TestModel())
        parent_agent = Agent(TestModel())

        @parent_agent.tool
        async def subagent_calculator(ctx, query: str) -> str:
            child_res = await child_agent.run(query)
            return f"Child response: 42"

        result = parent_agent.run_sync("Calculate math")
        self.assertIsNotNone(result.output)"""

text = text.replace(old_p6, new_p6)

with open("research/runs/evolution/pydantic_ai/test_pydantic_ai_evolution.py", "w", encoding="utf-8") as f:
    f.write(text)

print("Updated test_pydantic_ai_evolution.py")
