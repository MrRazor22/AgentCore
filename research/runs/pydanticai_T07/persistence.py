"""
Write-Ahead Logging (WAL) Durable Context Persistence for PydanticAI.
Task: T07 - Durable Context and Session Recovery.
"""
import os
import json
import time
from typing import List, Dict, Any, Optional
from pathlib import Path


class FileWalStore:
    """
    Durable append-only Write-Ahead Log (WAL) store for conversation events.
    Synchronously flushes to disk to survive crashes.
    """
    def __init__(self, directory: str, session_id: str = "default_session"):
        self.directory = Path(directory)
        self.session_id = session_id
        self.directory.mkdir(parents=True, exist_ok=True)
        self.wal_file = self.directory / f"{session_id}.wal"
        self.checkpoint_file = self.directory / f"{session_id}.checkpoint.json"

    def append_event(self, event_type: str, data: Dict[str, Any]) -> None:
        record = {
            "session_id": self.session_id,
            "event_type": event_type,
            "timestamp": time.time(),
            "data": data
        }
        line = json.dumps(record) + "\n"
        with open(self.wal_file, "a", encoding="utf-8") as f:
            f.write(line)
            f.flush()
            os.fsync(f.fileno())

    def checkpoint(self) -> None:
        """Flushes consolidated history into checkpoint snapshot and empties active WAL."""
        events = self.load_all_events()
        with open(self.checkpoint_file, "w", encoding="utf-8") as f:
            json.dump(events, f, indent=2)
            f.flush()
            os.fsync(f.fileno())
        # Clear WAL
        with open(self.wal_file, "w", encoding="utf-8") as f:
            f.truncate(0)

    def load_all_events(self) -> List[Dict[str, Any]]:
        """Recovers consolidated state from checkpoint + uncheckpointed WAL entries."""
        events: List[Dict[str, Any]] = []
        if self.checkpoint_file.exists():
            with open(self.checkpoint_file, "r", encoding="utf-8") as f:
                try:
                    events.extend(json.load(f))
                except Exception:
                    pass

        if self.wal_file.exists():
            with open(self.wal_file, "r", encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if line:
                        try:
                            events.append(json.loads(line))
                        except Exception:
                            pass
        return events


class PydanticAISessionManager:
    """Manages persistent conversational session for PydanticAI Agent."""
    def __init__(self, agent, store: FileWalStore):
        self.agent = agent
        self.store = store

    def run_turn(self, user_prompt: str) -> str:
        # 1. Write user prompt to WAL prior to LLM execution (Write-Ahead)
        self.store.append_event("user_prompt", {"content": user_prompt})

        # 2. Execute agent
        result = self.agent.run_sync(user_prompt)
        output_text = str(result.output)

        # 3. Write model response to WAL
        self.store.append_event("model_response", {"content": output_text})
        return output_text

    def get_conversation_history(self) -> List[Dict[str, str]]:
        events = self.store.load_all_events()
        history = []
        for e in events:
            if e["event_type"] == "user_prompt":
                history.append({"role": "user", "content": e["data"]["content"]})
            elif e["event_type"] == "model_response":
                history.append({"role": "assistant", "content": e["data"]["content"]})
        return history
