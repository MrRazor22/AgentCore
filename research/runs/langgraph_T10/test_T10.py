"""
Acceptance tests for Task T10: Real-time Stream Observation.
"""
import time
import unittest
from langchain_core.messages import HumanMessage, AIMessage
from langgraph.types import StreamWriter

try:
    from agent_graph import build_agent_graph
    from stream_observer import StreamObserver
except ImportError:
    from research.runs.langgraph_T10.agent_graph import build_agent_graph
    from research.runs.langgraph_T10.stream_observer import StreamObserver


class TestLangGraphT10StreamObservation(unittest.TestCase):
    def test_yields_token_deltas_in_real_time(self):
        graph = build_agent_graph()
        observer = StreamObserver()

        # Stream in custom mode to observe fine-grained chunks
        stream = graph.stream(
            {"messages": [HumanMessage(content="Hello streaming")]},
            stream_mode="custom"
        )

        received_chunks = list(observer.observe(stream))
        self.assertEqual(len(received_chunks), 4)
        self.assertEqual(observer.token_deltas, ["Response ", "from ", "baseline ", "model."])

    def test_passes_chunks_transparently_without_buffering(self):
        delays = []
        def slow_streaming_model(state, writer: StreamWriter):
            for word in ["alpha ", "beta ", "gamma "]:
                time.sleep(0.03)
                writer({"type": "token_chunk", "content": word})
            return {"messages": [AIMessage(content="alpha beta gamma ")]}

        graph = build_agent_graph(model_fn=slow_streaming_model)
        observer = StreamObserver()

        t0 = time.time()
        stream = graph.stream({"messages": [HumanMessage(content="test")]}, stream_mode="custom")

        for chunk in observer.observe(stream):
            # Record arrival time of each chunk as it arrives
            delays.append(time.time() - t0)

        # Chunks arrived incrementally over time (not all buffered at the end)
        self.assertEqual(len(delays), 3)
        self.assertGreater(delays[1], delays[0])
        self.assertGreater(delays[2], delays[1])

    def test_emits_distinct_events_for_content_tool_and_completion(self):
        def model_with_tool_and_content(state, writer: StreamWriter):
            writer({"type": "token_chunk", "content": "I will invoke a tool."})
            writer({"type": "tool_chunk", "tool_call": {"name": "calculator", "args": {"expr": "2+2"}}})
            return {"messages": [AIMessage(content="I will invoke a tool.")]}

        graph = build_agent_graph(model_fn=model_with_tool_and_content)
        observer = StreamObserver()

        stream = graph.stream({"messages": [HumanMessage(content="Compute 2+2")]}, stream_mode="custom")
        list(observer.observe(stream))

        event_types = [e.event_type for e in observer.events]
        self.assertIn("token_delta", event_types)
        self.assertIn("tool_call_delta", event_types)
        self.assertIn("completion", event_types)
        self.assertTrue(observer.completed)

    def test_does_not_duplicate_or_drop_stream_items(self):
        tokens = [f"tok_{i} " for i in range(15)]
        def deterministic_stream_model(state, writer: StreamWriter):
            for t in tokens:
                writer({"type": "token_chunk", "content": t})
            return {"messages": [AIMessage(content="".join(tokens))]}

        graph = build_agent_graph(model_fn=deterministic_stream_model)
        observer = StreamObserver()

        stream = graph.stream({"messages": [HumanMessage(content="count")]}, stream_mode="custom")
        downstream_received = list(observer.observe(stream))

        # Check downstream consumer received exactly all 15 items
        self.assertEqual(len(downstream_received), 15)
        # Check observer recorded exactly all 15 items without drop or duplication
        self.assertEqual(len(observer.token_deltas), 15)
        self.assertEqual("".join(observer.token_deltas), "".join(tokens))


if __name__ == "__main__":
    unittest.main()
