"""
Unit tests for OpenAI Agents SDK Durable Context and Session Recovery (Task T07).

Acceptance Criteria:
1. Appends incoming and generated message events to durable store synchronously.
2. Recovers full conversational history upon rehydration from disk/store.
3. Clears temporary WAL uncommitted chunks upon successful checkpoint.
4. Recovers gracefully without history truncation after simulated crash.
"""
import sys
import tempfile
import asyncio
import unittest
from pathlib import Path

CURRENT_DIR = Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

from wal_session import DurableWALSession
from agent_app import create_durable_agent, run_agent_in_session


class TestOpenAIAgentsDurableContextT07(unittest.TestCase):
    """Test suite for OpenAI Agents SDK Durable Context and WAL Recovery."""

    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.db_path = Path(self.temp_dir.name) / "test_session.db"

    def tearDown(self):
        self.temp_dir.cleanup()

    def test_appends_events_to_durable_store_synchronously(self):
        """Criterion 1: Appends incoming and generated message events to durable store synchronously."""
        session = DurableWALSession("session_01", self.db_path)
        agent = create_durable_agent()

        result = run_agent_in_session(agent, "Hello round 1", session=session)
        self.assertIn("Hello round 1", result.final_output)

        # Directly inspect items in session
        items = asyncio.run(session.get_items())
        self.assertGreaterEqual(len(items), 2)  # User input + assistant response

        # Verify persisted content
        user_items = [i for i in items if i.get("role") == "user" or "Hello round 1" in str(i)]
        assistant_items = [i for i in items if i.get("role") == "assistant" or "Echo" in str(i)]
        self.assertGreater(len(user_items), 0)
        self.assertGreater(len(assistant_items), 0)

    def test_recovers_full_history_upon_rehydration(self):
        """Criterion 2: Recovers full conversational history upon rehydration from disk/store."""
        # Session run 1
        session1 = DurableWALSession("session_rehydrate", self.db_path)
        agent1 = create_durable_agent()
        run_agent_in_session(agent1, "First message", session=session1)
        run_agent_in_session(agent1, "Second message", session=session1)

        # Rehydrate a new Session instance pointing to the same SQLite database file
        session2 = DurableWALSession("session_rehydrate", self.db_path)
        rehydrated_items = asyncio.run(session2.get_items())

        self.assertGreaterEqual(len(rehydrated_items), 4)
        history_str = str(rehydrated_items)
        self.assertIn("First message", history_str)
        self.assertIn("Second message", history_str)

        # Continue conversation with rehydrated session
        agent2 = create_durable_agent()
        res3 = run_agent_in_session(agent2, "Third message", session=session2)
        self.assertIn("Third message", res3.final_output)

    def test_clears_uncommitted_wal_chunks_upon_checkpoint(self):
        """Criterion 3: Clears temporary WAL uncommitted chunks upon successful checkpoint."""
        session = DurableWALSession("session_wal", self.db_path)

        # Simulate streaming chunk writes to WAL during in-flight generation
        session.append_wal_chunk({"type": "chunk", "delta": "Partial token 1"})
        session.append_wal_chunk({"type": "chunk", "delta": "Partial token 2"})

        uncommitted = session.get_uncommitted_wal_chunks()
        self.assertEqual(len(uncommitted), 2)
        self.assertTrue(session.wal_path.exists())

        # Checkpoint commits state and purges in-flight WAL chunk file
        session.checkpoint()

        self.assertFalse(session.wal_path.exists())
        self.assertEqual(len(session.get_uncommitted_wal_chunks()), 0)

    def test_recovers_gracefully_without_history_truncation_after_crash(self):
        """Criterion 4: Recovers gracefully without history truncation after simulated crash."""
        session = DurableWALSession("session_crash", self.db_path)
        agent = create_durable_agent()

        # Turn 1 completes and commits
        run_agent_in_session(agent, "Turn 1 stable", session=session)

        # Turn 2 in progress: WAL chunk appended, but process crashes before checkpoint
        session.append_wal_chunk({"type": "chunk", "delta": "In-flight uncommitted stream before crash"})

        # Simulate abrupt process crash: discard memory state, re-open from disk
        crashed_session = DurableWALSession("session_crash", self.db_path)

        # Re-opened session preserves full committed historical turns
        items = asyncio.run(crashed_session.get_items())
        history_str = str(items)
        self.assertIn("Turn 1 stable", history_str)

        # Uncommitted chunk is isolated in WAL and recoverable for diagnostics without corrupting history
        uncommitted = crashed_session.get_uncommitted_wal_chunks()
        self.assertEqual(len(uncommitted), 1)
        self.assertIn("In-flight uncommitted stream", uncommitted[0]["delta"])

        # Resume cleanly with next prompt
        res_resumed = run_agent_in_session(agent, "Turn 2 after crash", session=crashed_session)
        self.assertIn("Turn 2 after crash", res_resumed.final_output)

        # Post-resume history contains all committed turns without truncation
        final_items = asyncio.run(crashed_session.get_items())
        final_history = str(final_items)
        self.assertIn("Turn 1 stable", final_history)
        self.assertIn("Turn 2 after crash", final_history)


if __name__ == "__main__":
    unittest.main()
