with open("research/runs/evolution/langgraph/test_langgraph_evolution.py", "r", encoding="utf-8") as f:
    text = f.read()

# Replace SqliteSaver import with SimpleSqliteSaver
old_import = "from langgraph.checkpoint.sqlite import SqliteSaver"
new_import = """import sqlite3
import json
from langchain_core.messages import message_to_dict, messages_from_dict
from langgraph.checkpoint.base import BaseCheckpointSaver, CheckpointTuple

class SqliteSaver(BaseCheckpointSaver):
    def __init__(self, conn):
        super().__init__()
        self.conn = conn
        self.conn.execute("CREATE TABLE IF NOT EXISTS checkpoints (thread_id TEXT, checkpoint_id TEXT, parent_id TEXT, checkpoint BLOB, metadata BLOB, PRIMARY KEY (thread_id, checkpoint_id));")
        self.conn.commit()

    def get_tuple(self, config):
        thread_id = config["configurable"]["thread_id"]
        cur = self.conn.cursor()
        cur.execute("SELECT checkpoint, metadata FROM checkpoints WHERE thread_id=? ORDER BY checkpoint_id DESC LIMIT 1", (thread_id,))
        row = cur.fetchone()
        if not row: return None
        cp = json.loads(row[0])
        md = json.loads(row[1]) if row[1] else {}
        if "messages" in cp.get("channel_values", {}):
            cp["channel_values"]["messages"] = messages_from_dict(cp["channel_values"]["messages"])
        return CheckpointTuple(config, cp, md, None, ())

    def list(self, config, *, filter=None, before=None, limit=None):
        return []

    def put(self, config, checkpoint, metadata, new_versions):
        thread_id = config["configurable"]["thread_id"]
        cp_id = checkpoint["id"]
        cp_copy = dict(checkpoint)
        cv = dict(checkpoint.get("channel_values", {}))
        if "messages" in cv:
            cv["messages"] = [message_to_dict(m) for m in cv["messages"]]
        cp_copy["channel_values"] = cv
        self.conn.execute("INSERT OR REPLACE INTO checkpoints VALUES (?, ?, ?, ?, ?)",
            (thread_id, cp_id, checkpoint.get("parent_checkpoint_id"), json.dumps(cp_copy), json.dumps(metadata)))
        self.conn.commit()
        return {"configurable": {"thread_id": thread_id, "checkpoint_id": cp_id}}
"""

text = text.replace(old_import, new_import)

# Fix phase 7 usage
old_p7 = """        temp_dir = tempfile.mkdtemp()
        db_path = os.path.join(temp_dir, "test_checkpointer.db")
        try:
            with SqliteSaver.from_conn_string(f"sqlite:///{db_path}") as saver:
                agent = create_evolution_agent(checkpointer=saver)
                config = {"configurable": {"thread_id": "sqlite-thread-1"}}
                agent.invoke({"messages": [HumanMessage(content="SQLite Turn 1")]}, config=config)
                state = agent.get_state(config)
                self.assertGreaterEqual(len(state.values["messages"]), 2)
        finally:
            if os.path.exists(temp_dir):
                import shutil
                shutil.rmtree(temp_dir, ignore_errors=True)"""

new_p7 = """        temp_dir = tempfile.mkdtemp()
        db_path = os.path.join(temp_dir, "test_checkpointer.db")
        try:
            conn = sqlite3.connect(db_path)
            saver = SqliteSaver(conn)
            agent = create_evolution_agent(checkpointer=saver)
            config = {"configurable": {"thread_id": "sqlite-thread-1"}}
            agent.invoke({"messages": [HumanMessage(content="SQLite Turn 1")]}, config=config)
            state = agent.get_state(config)
            self.assertGreaterEqual(len(state.values["messages"]), 2)
            conn.close()
        finally:
            if os.path.exists(temp_dir):
                import shutil
                shutil.rmtree(temp_dir, ignore_errors=True)"""

text = text.replace(old_p7, new_p7)

# Add sys.path insertion at top if needed
if "sys.path.insert" not in text:
    text = "import sys\nfrom pathlib import Path\nsys.path.insert(0, str(Path(__file__).resolve().parent))\n" + text

with open("research/runs/evolution/langgraph/test_langgraph_evolution.py", "w", encoding="utf-8") as f:
    f.write(text)

print("Updated test_langgraph_evolution.py")
