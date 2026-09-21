with open("research/runs/evolution/langgraph/test_langgraph_evolution.py", "r", encoding="utf-8") as f:
    text = f.read()

text = text.replace("    def list(self, config, *, filter=None, before=None, limit=None):\n        return []",
"""    def list(self, config, *, filter=None, before=None, limit=None):
        return []

    def put_writes(self, config, writes, task_id):
        pass""")

with open("research/runs/evolution/langgraph/test_langgraph_evolution.py", "w", encoding="utf-8") as f:
    f.write(text)

print("Implemented put_writes in SqliteSaver")
