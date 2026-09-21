"""
Acceptance tests for Task T02: Structured Telemetry and Logging in LangGraph.
"""
import unittest
from langchain_core.messages import HumanMessage, AIMessage
try:
    from agent_graph import build_agent_graph
    from telemetry_handler import StructuredTelemetryHandler
except ImportError:
    from research.runs.langgraph_T02.agent_graph import build_agent_graph
    from research.runs.langgraph_T02.telemetry_handler import StructuredTelemetryHandler


class TestLangGraphT02Telemetry(unittest.TestCase):
    def test_emits_llm_events_with_duration_and_tokens(self):
        telemetry = StructuredTelemetryHandler(caller_id="agent_tester")
        graph = build_agent_graph(telemetry_handler=telemetry)

        result = graph.invoke({"messages": [HumanMessage(content="Hello telemetry")]})
        self.assertIn("messages", result)

        event_types = [e["event_type"] for e in telemetry.events]
        self.assertIn("llm_start", event_types)
        self.assertIn("llm_end", event_types)

        end_event = next(e for e in telemetry.events if e["event_type"] == "llm_end")
        self.assertIn("duration_ms", end_event)
        self.assertGreaterEqual(end_event["duration_ms"], 0.0)
        self.assertIn("token_usage", end_event)
        self.assertGreater(end_event["token_usage"]["total_tokens"], 0)
        self.assertEqual(end_event["caller_id"], "agent_tester")

    def test_emits_tool_events_with_status_and_duration(self):
        telemetry = StructuredTelemetryHandler(caller_id="tool_tester")

        turn = 0
        def tool_calling_model(state):
            nonlocal turn
            turn += 1
            if turn == 1:
                return {
                    "messages": [
                        AIMessage(
                            content="",
                            tool_calls=[{"name": "search", "args": {"query": "LangGraph T02"}, "id": "call_1"}]
                        )
                    ]
                }
            return {"messages": [AIMessage(content="Tool execution complete")]}

        def search_tool(query: str) -> str:
            """Search tool."""
            return f"Result for {query}"

        graph = build_agent_graph(
            model_fn=tool_calling_model,
            tools=[search_tool],
            telemetry_handler=telemetry
        )

        result = graph.invoke({"messages": [HumanMessage(content="Find info")]})
        self.assertIn("messages", result)

        tool_start = next((e for e in telemetry.events if e["event_type"] == "tool_start"), None)
        tool_end = next((e for e in telemetry.events if e["event_type"] == "tool_end"), None)

        self.assertIsNotNone(tool_start)
        self.assertIsNotNone(tool_end)
        self.assertEqual(tool_start["tool_name"], "search")
        self.assertEqual(tool_end["status"], "success")
        self.assertGreaterEqual(tool_end["duration_ms"], 0.0)

    def test_payloads_passed_unmutated(self):
        telemetry = StructuredTelemetryHandler()
        original_prompt = "Exact user query without mutation"
        graph = build_agent_graph(telemetry_handler=telemetry)

        result = graph.invoke({"messages": [HumanMessage(content=original_prompt)]})
        messages = result["messages"]
        self.assertEqual(messages[0].content, original_prompt)
        self.assertEqual(messages[1].content, "Response from baseline model.")

    def test_secrets_redacted_in_structured_logs(self):
        telemetry = StructuredTelemetryHandler()
        graph = build_agent_graph(telemetry_handler=telemetry)

        secret_input = "Please use api_key sk-1234567890abcdef to authenticate"
        result = graph.invoke({"messages": [HumanMessage(content=secret_input)]})

        start_event = next(e for e in telemetry.events if e["event_type"] == "llm_start")
        raw_log = str(start_event)
        self.assertNotIn("sk-1234567890abcdef", raw_log)
        self.assertIn("***REDACTED***", raw_log)


if __name__ == "__main__":
    unittest.main()
