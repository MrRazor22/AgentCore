"""
Acceptance tests for Task T04: Response and Semantic Caching.
"""
import time
import unittest
from langchain_core.messages import HumanMessage, AIMessage, SystemMessage

try:
    from agent_graph import build_agent_graph
    from response_cache import ResponseCache
except ImportError:
    from research.runs.langgraph_T04.agent_graph import build_agent_graph
    from research.runs.langgraph_T04.response_cache import ResponseCache


class TestLangGraphT04Caching(unittest.TestCase):
    def test_cache_hit_bypasses_model_invocation(self):
        call_count = 0
        def model_fn(state):
            nonlocal call_count
            call_count += 1
            return {"messages": [AIMessage(content=f"Computed response {call_count}")]}

        cache = ResponseCache(ttl_seconds=60)
        graph = build_agent_graph(model_fn=model_fn, cache=cache)

        # First call -> miss
        res1 = graph.invoke({"messages": [HumanMessage(content="Hello cache")]})
        self.assertEqual(call_count, 1)
        self.assertEqual(res1["messages"][-1].content, "Computed response 1")

        # Second call with identical prompt -> hit
        res2 = graph.invoke({"messages": [HumanMessage(content="Hello cache")]})
        self.assertEqual(call_count, 1)
        self.assertEqual(res2["messages"][-1].content, "Computed response 1")

    def test_cache_miss_invokes_model_and_populates(self):
        call_count = 0
        def model_fn(state):
            nonlocal call_count
            call_count += 1
            return {"messages": [AIMessage(content=f"Result {call_count}")]}

        cache = ResponseCache(ttl_seconds=60)
        graph = build_agent_graph(model_fn=model_fn, cache=cache)

        graph.invoke({"messages": [HumanMessage(content="Query A")]})
        self.assertEqual(call_count, 1)

        graph.invoke({"messages": [HumanMessage(content="Query B")]})
        self.assertEqual(call_count, 2)

    def test_key_derivation_includes_history_and_system_prompt(self):
        cache = ResponseCache(ttl_seconds=60)
        msg = HumanMessage(content="Explain quantum computing")

        key1 = cache.derive_key([msg], system_prompt="You are a physics expert.")
        key2 = cache.derive_key([msg], system_prompt="You are a 5-year-old child.")
        key3 = cache.derive_key([HumanMessage(content="Previous turn"), msg], system_prompt="You are a physics expert.")

        self.assertNotEqual(key1, key2)
        self.assertNotEqual(key1, key3)
        self.assertNotEqual(key2, key3)

    def test_ttl_expiration_bypasses_cache(self):
        call_count = 0
        def model_fn(state):
            nonlocal call_count
            call_count += 1
            return {"messages": [AIMessage(content=f"Fresh response {call_count}")]}

        # Short TTL of 0.1s
        cache = ResponseCache(ttl_seconds=0.1)
        graph = build_agent_graph(model_fn=model_fn, cache=cache)

        res1 = graph.invoke({"messages": [HumanMessage(content="Fast TTL")]})
        self.assertEqual(call_count, 1)

        # Wait for TTL expiry
        time.sleep(0.15)

        res2 = graph.invoke({"messages": [HumanMessage(content="Fast TTL")]})
        self.assertEqual(call_count, 2)
        self.assertEqual(res2["messages"][-1].content, "Fresh response 2")


if __name__ == "__main__":
    unittest.main()
