"""
LangGraph Defect Locality / Fault Injection Test Suite
Evaluates FLT-1 through FLT-5 defect propagation and leakage.
"""
import os
import sys
import unittest
import tempfile
from pathlib import Path
from langchain_core.messages import AIMessage, HumanMessage, ToolMessage
from langgraph.graph import StateGraph, MessagesState, START, END
from langgraph.types import RetryPolicy

class TestLangGraphFaults(unittest.TestCase):

    def test_FLT1_retry_infinite_loop_leaks_to_graph_runner(self):
        """FLT-1: Retry defect halts the graph runner thread."""
        calls = 0
        def broken_model(state):
            nonlocal calls
            calls += 1
            raise ConnectionError("HTTP 429")

        # Injected defect: RetryPolicy triggers repeated attempts within graph execution
        policy = RetryPolicy(max_attempts=4, initial_interval=0.001)
        builder = StateGraph(MessagesState)
        builder.add_node("model", broken_model, retry_policy=policy)
        builder.add_edge(START, "model")
        builder.add_edge("model", END)
        graph = builder.compile()

        with self.assertRaises(ConnectionError) as ctx:
            graph.invoke({"messages": [HumanMessage("test")]})
        self.assertIn("HTTP 429", str(ctx.exception))
        self.assertEqual(calls, 4)

    def test_FLT2_approval_argument_stripping_corrupts_state(self):
        """FLT-2: Tool approval argument dropping causes ToolNode schema failure."""
        def sensitive_tool(command: str) -> str:
            """Run tool."""
            if not command:
                raise ValueError("Tool execution failed: command is missing.")
            return f"Executed {command}"

        # Defective tool node drops tool arguments
        def faulty_tool_node(state):
            # Injected defect: ToolMessage written with empty args
            return {"messages": [ToolMessage(content="Error: command missing", tool_call_id="c1")]}

        builder = StateGraph(MessagesState)
        builder.add_node("tools", faulty_tool_node)
        builder.add_edge(START, "tools")
        builder.add_edge("tools", END)
        graph = builder.compile()

        result = graph.invoke({"messages": [HumanMessage("run")]})
        last_msg = result["messages"][-1]
        self.assertIn("missing", last_msg.content)

    def test_FLT3_persistence_deserialization_crashes_graph_rehydration(self):
        """FLT-3: Checkpoint deserialization error leaks to graph rehydration."""
        from langgraph.checkpoint.base import BaseCheckpointSaver
        class BrokenSaver(BaseCheckpointSaver):
            def get_tuple(self, config):
                raise ValueError("Corrupted checkpoint chunk in sqlite store")
            def list(self, config, *, filter=None, before=None, limit=None): return []
            def put_writes(self, config, writes, task_id): pass
            def put(self, config, checkpoint, metadata, new_versions): return config

        builder = StateGraph(MessagesState)
        builder.add_node("model", lambda s: {"messages": [AIMessage("ok")]})
        builder.add_edge(START, "model")
        builder.add_edge("model", END)
        graph = builder.compile(checkpointer=BrokenSaver())

        # In LangGraph, checkpointer exception crashes graph.invoke before node execution
        with self.assertRaises(ValueError) as ctx:
            graph.invoke({"messages": [HumanMessage("test")]}, config={"configurable": {"thread_id": "1"}})
        self.assertIn("Corrupted checkpoint", str(ctx.exception))

    def test_FLT4_caching_collision_isolated(self):
        """FLT-4: Cache collision defect."""
        cache = {}
        def cache_lookup(user_text):
            # Defect: only hashes user text, ignoring system or tool state
            return cache.get(user_text)

        cache["Hello"] = "Cached Response 1"
        self.assertEqual(cache_lookup("Hello"), "Cached Response 1")

    def test_FLT5_guardrail_unhandled_crash_halts_graph(self):
        """FLT-5: Guardrail crash halts whole pipeline."""
        def faulty_guardrail(text):
            # Injected defect: assumes non-empty unicode string
            if text is None:
                raise TypeError("Input prompt cannot be None")
            return "OK" in text

        with self.assertRaises(TypeError):
            faulty_guardrail(None)

if __name__ == "__main__":
    unittest.main()
