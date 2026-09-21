"""
Write-Ahead Logging (WAL) Durable Checkpointer for LangGraph.
"""
import os
import json
import pickle
from typing import Any, Dict, Iterator, List, Optional, Sequence
from langchain_core.runnables import RunnableConfig
from langgraph.checkpoint.base import (
    BaseCheckpointSaver,
    Checkpoint,
    CheckpointMetadata,
    CheckpointTuple,
    ChannelVersions,
)
from langgraph.checkpoint.memory import MemorySaver


def to_plain(d):
    if isinstance(d, dict):
        return {k: to_plain(v) for k, v in d.items()}
    elif isinstance(d, (list, tuple)):
        return [to_plain(v) for v in d]
    return d


class WALCheckpointer(MemorySaver):
    def __init__(self, storage_dir: str):
        super().__init__()
        self.storage_dir = storage_dir
        os.makedirs(self.storage_dir, exist_ok=True)
        self.wal_path = os.path.join(self.storage_dir, "wal.log")
        self.snapshot_path = os.path.join(self.storage_dir, "snapshot.pkl")
        self.uncommitted_writes_count = 0
        self._load_from_storage()

    def _load_from_storage(self):
        if os.path.exists(self.snapshot_path):
            with open(self.snapshot_path, "rb") as f:
                data = pickle.load(f)
                for t_id, ns_dict in data.get("storage", {}).items():
                    for ns, c_dict in ns_dict.items():
                        self.storage[t_id][ns].update(c_dict)
                for t_id, w_dict in data.get("writes", {}).items():
                    self.writes[t_id].update(w_dict)
                self.blobs.update(data.get("blobs", {}))

    def _save_snapshot(self):
        data = {
            "storage": to_plain(self.storage),
            "writes": to_plain(self.writes),
            "blobs": to_plain(self.blobs),
        }
        with open(self.snapshot_path, "wb") as f:
            pickle.dump(data, f)

    def put_writes(
        self,
        config: RunnableConfig,
        writes: Sequence[tuple[str, Any]],
        task_id: str,
        task_path: str = "",
    ) -> None:
        super().put_writes(config, writes, task_id, task_path=task_path)
        self.uncommitted_writes_count += len(writes)
        thread_id = config.get("configurable", {}).get("thread_id", "default")
        log_entry = {
            "op": "uncommitted_write",
            "thread_id": thread_id,
            "task_id": task_id,
            "count": len(writes),
        }
        with open(self.wal_path, "a", encoding="utf-8") as f:
            f.write(json.dumps(log_entry) + "\n")

    def put(
        self,
        config: RunnableConfig,
        checkpoint: Checkpoint,
        metadata: CheckpointMetadata,
        new_versions: ChannelVersions,
    ) -> RunnableConfig:
        res = super().put(config, checkpoint, metadata, new_versions)
        self.uncommitted_writes_count = 0
        thread_id = config.get("configurable", {}).get("thread_id", "default")
        checkpoint_id = checkpoint["id"]
        log_entry = {
            "op": "commit_checkpoint",
            "thread_id": thread_id,
            "checkpoint_id": checkpoint_id,
        }
        with open(self.wal_path, "a", encoding="utf-8") as f:
            f.write(json.dumps(log_entry) + "\n")

        self._save_snapshot()
        return res
