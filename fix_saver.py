with open("research/runs/evolution/langgraph/test_langgraph_evolution.py", "r", encoding="utf-8") as f:
    text = f.read()

old_put = """        thread_id = config["configurable"]["thread_id"]
        cp_id = checkpoint["id"]
        cp_copy = dict(checkpoint)
        cv = dict(checkpoint.get("channel_values", {}))
        if "messages" in cv:
            cv["messages"] = [message_to_dict(m) for m in cv["messages"]]
        cp_copy["channel_values"] = cv
        self.conn.execute("INSERT OR REPLACE INTO checkpoints VALUES (?, ?, ?, ?, ?)",
            (thread_id, cp_id, checkpoint.get("parent_checkpoint_id"), json.dumps(cp_copy), json.dumps(metadata)))
        self.conn.commit()
        return {"configurable": {"thread_id": thread_id, "checkpoint_id": cp_id}}"""

new_put = """        import pickle
        thread_id = config["configurable"]["thread_id"]
        cp_id = checkpoint["id"]
        self.conn.execute("INSERT OR REPLACE INTO checkpoints VALUES (?, ?, ?, ?, ?)",
            (thread_id, cp_id, checkpoint.get("parent_checkpoint_id"), pickle.dumps(checkpoint), pickle.dumps(metadata)))
        self.conn.commit()
        return {"configurable": {"thread_id": thread_id, "checkpoint_id": cp_id}}"""

old_get = """        thread_id = config["configurable"]["thread_id"]
        cur = self.conn.cursor()
        cur.execute("SELECT checkpoint, metadata FROM checkpoints WHERE thread_id=? ORDER BY checkpoint_id DESC LIMIT 1", (thread_id,))
        row = cur.fetchone()
        if not row: return None
        cp = json.loads(row[0])
        md = json.loads(row[1]) if row[1] else {}
        if "messages" in cp.get("channel_values", {}):
            cp["channel_values"]["messages"] = messages_from_dict(cp["channel_values"]["messages"])
        return CheckpointTuple(config, cp, md, None, ())"""

new_get = """        import pickle
        thread_id = config["configurable"]["thread_id"]
        cur = self.conn.cursor()
        cur.execute("SELECT checkpoint, metadata FROM checkpoints WHERE thread_id=? ORDER BY checkpoint_id DESC LIMIT 1", (thread_id,))
        row = cur.fetchone()
        if not row: return None
        cp = pickle.loads(row[0])
        md = pickle.loads(row[1]) if row[1] else {}
        return CheckpointTuple(config, cp, md, None, ())"""

text = text.replace(old_put, new_put)
text = text.replace(old_get, new_get)

with open("research/runs/evolution/langgraph/test_langgraph_evolution.py", "w", encoding="utf-8") as f:
    f.write(text)

print("Updated SqliteSaver to use pickle for arbitrary channel values")
