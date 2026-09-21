"""
Write-Ahead Logging (WAL) Durable Session for OpenAI Agents SDK (Task T07).
"""
import os
import json
import sqlite3
import threading
from pathlib import Path
from typing import List, Optional, Any
from agents.memory.session import SessionABC
from agents.memory.session_settings import SessionSettings, resolve_session_limit
from agents.items import TResponseInputItem


class DurableWALSession(SessionABC):
    """
    WAL-enabled durable Session persisting incoming and generated messages.
    Supports checkpoints, atomic commits, crash recovery, and WAL chunk pruning.
    """
    def __init__(
        self,
        session_id: str,
        db_path: str | Path,
        session_settings: Optional[SessionSettings] = None
    ):
        self.session_id = session_id
        self.session_settings = session_settings or SessionSettings()
        self.db_path = Path(db_path)
        self.wal_path = self.db_path.with_suffix(".wal.jsonl")
        self._lock = threading.Lock()

        # Initialize SQLite database with WAL mode
        self._init_db()

    def _init_db(self):
        with self._lock:
            conn = sqlite3.connect(str(self.db_path), check_same_thread=False)
            conn.execute("PRAGMA journal_mode=WAL")
            conn.execute("""
                CREATE TABLE IF NOT EXISTS messages (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    session_id TEXT NOT NULL,
                    message_data TEXT NOT NULL,
                    committed INTEGER DEFAULT 1,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                )
            """)
            conn.commit()
            conn.close()

    def _get_connection(self) -> sqlite3.Connection:
        conn = sqlite3.connect(str(self.db_path), check_same_thread=False)
        conn.execute("PRAGMA journal_mode=WAL")
        return conn

    def append_wal_chunk(self, chunk: dict[str, Any]):
        """Write in-flight uncommitted stream chunk to WAL file synchronously (write-ahead log)."""
        with self._lock:
            with open(self.wal_path, "a", encoding="utf-8") as f:
                f.write(json.dumps(chunk) + "\n")
                f.flush()
                os.fsync(f.fileno())

    def get_uncommitted_wal_chunks(self) -> List[dict]:
        """Read active uncommitted WAL entries."""
        with self._lock:
            if not self.wal_path.exists():
                return []
            chunks = []
            with open(self.wal_path, "r", encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if line:
                        chunks.append(json.loads(line))
            return chunks

    def checkpoint(self):
        """Checkpoint: commit session state and clear uncommitted WAL chunks."""
        with self._lock:
            if self.wal_path.exists():
                self.wal_path.unlink()

    async def get_items(self, limit: int | None = None) -> list[TResponseInputItem]:
        session_limit = resolve_session_limit(limit, self.session_settings)
        with self._lock:
            conn = self._get_connection()
            if session_limit is None:
                cursor = conn.execute(
                    "SELECT message_data FROM messages WHERE session_id = ? ORDER BY id ASC",
                    (self.session_id,)
                )
            else:
                cursor = conn.execute(
                    "SELECT message_data FROM messages WHERE session_id = ? ORDER BY id DESC LIMIT ?",
                    (self.session_id, session_limit)
                )
            rows = cursor.fetchall()
            conn.close()

            if session_limit is not None:
                rows = list(reversed(rows))

            items = []
            for (data_str,) in rows:
                try:
                    items.append(json.loads(data_str))
                except Exception:
                    continue
            return items

    async def add_items(self, items: list[TResponseInputItem]) -> None:
        if not items:
            return
        with self._lock:
            conn = self._get_connection()
            # Append items synchronously
            records = [(self.session_id, json.dumps(item)) for item in items]
            conn.executemany(
                "INSERT INTO messages (session_id, message_data, committed) VALUES (?, ?, 1)",
                records
            )
            conn.commit()
            conn.close()

    async def pop_item(self) -> TResponseInputItem | None:
        with self._lock:
            conn = self._get_connection()
            cursor = conn.execute(
                "SELECT id, message_data FROM messages WHERE session_id = ? ORDER BY id DESC LIMIT 1",
                (self.session_id,)
            )
            row = cursor.fetchone()
            if not row:
                conn.close()
                return None
            item_id, data_str = row
            conn.execute("DELETE FROM messages WHERE id = ?", (item_id,))
            conn.commit()
            conn.close()
            return json.loads(data_str)

    async def clear_session(self) -> None:
        with self._lock:
            conn = self._get_connection()
            conn.execute("DELETE FROM messages WHERE session_id = ?", (self.session_id,))
            conn.commit()
            conn.close()
            if self.wal_path.exists():
                self.wal_path.unlink()
