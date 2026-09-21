"""
Acceptance tests for Task T07: Durable Context and Session Recovery (WAL).
"""
import os
import shutil
import tempfile
import unittest
from langchain_core.messages import HumanMessage, AIMessage

try:
    from agent_graph import build_agent_graph
    from wal_checkpointer import WALCheckpointer
except ImportError:
    from research.runs.langgraph_T07.agent_graph import build_agent_graph
    from research.runs.langgraph_T07.wal_checkpointer import WALCheckpointer


class TestLangGraphT07Persistence(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.mkdtemp()

    def tearDown(self):
        shutil.rmtree(self.temp_dir, ignore_errors=True)

    def test_appends_events_to_durable_store_synchronously(self):
        checkpointer = WALCheckpointer(storage_dir=self.temp_dir)
        graph = build_agent_graph(checkpointer=checkpointer)

        config = {"configurable": {"thread_id": "wal_thread_1"}}
        graph.invoke({"messages": [HumanMessage(content="Hello WAL")]}, config=config)

        wal_file = os.path.join(self.temp_dir, "wal.log")
        self.assertTrue(os.path.exists(wal_file))
        with open(wal_file, "r", encoding="utf-8") as f:
            lines = [line.strip() for line in f if line.strip()]

        self.assertGreater(len(lines), 0)
        self.assertTrue(any("commit_checkpoint" in line for line in lines))

    def test_recovers_full_conversational_history_upon_rehydration(self):
        # 1. Run first turn
        checkpointer1 = WALCheckpointer(storage_dir=self.temp_dir)
        graph1 = build_agent_graph(checkpointer=checkpointer1)
        config = {"configurable": {"thread_id": "thread_rehydrate"}}
        graph1.invoke({"messages": [HumanMessage(content="Turn 1 user message")]}, config=config)

        # 2. Simulate process restart by creating new checkpointer and graph on same store
        checkpointer2 = WALCheckpointer(storage_dir=self.temp_dir)
        graph2 = build_agent_graph(checkpointer=checkpointer2)

        state = graph2.get_state(config)
        messages = state.values.get("messages", [])
        self.assertEqual(len(messages), 2)
        self.assertEqual(messages[0].content, "Turn 1 user message")
        self.assertEqual(messages[1].content, "Response from baseline model.")

        # 3. Continue conversation on recovered graph
        graph2.invoke({"messages": [HumanMessage(content="Turn 2 user message")]}, config=config)
        state2 = graph2.get_state(config)
        messages2 = state2.values.get("messages", [])
        self.assertEqual(len(messages2), 4)
        self.assertEqual(messages2[2].content, "Turn 2 user message")

    def test_clears_uncommitted_writes_on_successful_checkpoint(self):
        checkpointer = WALCheckpointer(storage_dir=self.temp_dir)
        graph = build_agent_graph(checkpointer=checkpointer)
        config = {"configurable": {"thread_id": "thread_uncommitted"}}

        graph.invoke({"messages": [HumanMessage(content="Test uncommitted")]}, config=config)
        # When execution finishes turn, all pending writes are committed
        self.assertEqual(checkpointer.uncommitted_writes_count, 0)

    def test_recovers_gracefully_after_simulated_crash(self):
        # Run turn 1
        cp1 = WALCheckpointer(storage_dir=self.temp_dir)
        g1 = build_agent_graph(checkpointer=cp1)
        config = {"configurable": {"thread_id": "crash_recovery_thread"}}
        g1.invoke({"messages": [HumanMessage(content="Pre-crash turn")]}, config=config)

        # Simulate crash by deleting in-memory object and dropping unpersisted memory
        del cp1
        del g1

        # Rehydrate in new process session
        cp2 = WALCheckpointer(storage_dir=self.temp_dir)
        g2 = build_agent_graph(checkpointer=cp2)

        # Verify state is fully intact without truncation
        state = g2.get_state(config)
        self.assertIsNotNone(state.values)
        self.assertEqual(state.values["messages"][0].content, "Pre-crash turn")
        self.assertEqual(state.values["messages"][1].content, "Response from baseline model.")


if __name__ == "__main__":
    unittest.main()
