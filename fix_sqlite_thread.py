with open("research/runs/evolution/langgraph/test_langgraph_evolution.py", "r", encoding="utf-8") as f:
    text = f.read()

text = text.replace("conn = sqlite3.connect(db_path)", "conn = sqlite3.connect(db_path, check_same_thread=False)")

with open("research/runs/evolution/langgraph/test_langgraph_evolution.py", "w", encoding="utf-8") as f:
    f.write(text)

print("Added check_same_thread=False to sqlite3.connect")
