"""
Unit tests for PydanticAI WAL persistence (Task T07).

Verifies:
1. Appends incoming and generated message events to durable store synchronously.
2. Recovers full conversational history upon rehydration from disk/store.
3. Clears temporary WAL uncommitted chunks upon successful checkpoint.
4. Recovers gracefully without history truncation after simulated crash.
"""
import sys
import shutil
import tempfile
import unittest
from pathlib import Path

CURRENT_DIR = Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

from pydantic_ai.messages import ModelResponse, TextPart
from agent import create_agent
from persistence import FileWalStore, PydanticAISessionManager


class TestPydanticAIPersistenceT07(unittest.TestCase):
    """Test suite for PydanticAI Durable Context and Session Recovery."""

    def setUp(self):
        self.test_dir = tempfile.mkdtemp()

    def tearDown(self):
        shutil.rmtree(self.test_dir, ignore_errors=True)

    def test_synchronous_wal_append(self):
        """Test 1: Appends prompt and response events to WAL synchronously."""
        session = create_agent(wal_dir=self.test_dir, session_id="test_sync")
        output = session.run_turn("What is our project goal?")

        wal_file = Path(self.test_dir) / "test_sync.wal"
        self.assertTrue(wal_file.exists())
        self.assertGreater(wal_file.stat().st_size, 0)

        history = session.get_conversation_history()
        self.assertEqual(len(history), 2)
        self.assertEqual(history[0]["role"], "user")
        self.assertEqual(history[0]["content"], "What is our project goal?")
        self.assertEqual(history[1]["role"], "assistant")
        self.assertEqual(history[1]["content"], output)

    def test_recovery_across_instances(self):
        """Test 2: Recovers full conversational history upon rehydration from disk."""
        session1 = create_agent(wal_dir=self.test_dir, session_id="session_persist")
        session1.run_turn("Turn 1 message")
        session1.run_turn("Turn 2 message")

        # Simulate process termination by instantiating new agent/session on same directory
        session2 = create_agent(wal_dir=self.test_dir, session_id="session_persist")
        recovered_history = session2.get_conversation_history()

        self.assertEqual(len(recovered_history), 4)
        self.assertEqual(recovered_history[0]["content"], "Turn 1 message")
        self.assertEqual(recovered_history[2]["content"], "Turn 2 message")

    def test_checkpoint_clears_wal_chunks(self):
        """Test 3: Clears temporary WAL uncommitted chunks upon successful checkpoint."""
        session = create_agent(wal_dir=self.test_dir, session_id="chk_test")
        session.run_turn("Initial query")

        wal_file = Path(self.test_dir) / "chk_test.wal"
        checkpoint_file = Path(self.test_dir) / "chk_test.checkpoint.json"

        self.assertGreater(wal_file.stat().st_size, 0)
        self.assertFalse(checkpoint_file.exists())

        # Checkpoint
        session.store.checkpoint()

        self.assertEqual(wal_file.stat().st_size, 0)  # Emptied
        self.assertTrue(checkpoint_file.exists())
        self.assertGreater(checkpoint_file.stat().st_size, 0)

        # Full history still available
        history = session.get_conversation_history()
        self.assertEqual(len(history), 2)

    def test_simulated_crash_recovery(self):
        """Test 4: Recovers gracefully without history truncation after simulated crash."""
        session = create_agent(wal_dir=self.test_dir, session_id="crash_test")
        session.run_turn("Completed turn")

        # Simulate sudden crash mid-turn after writing user prompt
        session.store.append_event("user_prompt", {"content": "In-flight prompt before SIGKILL"})

        # New session rehydrates
        rehydrated = create_agent(wal_dir=self.test_dir, session_id="crash_test")
        history = rehydrated.get_conversation_history()

        # Both the completed turn and the in-flight prompt exist in log
        self.assertEqual(len(history), 3)
        self.assertEqual(history[0]["content"], "Completed turn")
        self.assertEqual(history[2]["content"], "In-flight prompt before SIGKILL")


if __name__ == "__main__":
    unittest.main()
