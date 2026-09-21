"""
Unit tests for PydanticAI structured telemetry (Task T02).

Verifies:
1. Emits event before and after LLM generation with duration and token usage.
2. Emits event before and after tool execution with execution status and duration.
3. Passes through execution payloads without mutating model or tool events.
4. Does not leak credentials or unmasked secrets in structured logs.
"""
import json
import sys
import unittest
from pathlib import Path

CURRENT_DIR = Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

from pydantic_ai import RunContext
from pydantic_ai.messages import ModelResponse, TextPart
from agent import create_agent
from telemetry import StructuredTelemetryHandler, sanitize_payload


class TestPydanticAITelemetryT02(unittest.TestCase):
    """Test suite for PydanticAI telemetry layer."""

    def test_llm_telemetry_emitted(self):
        """Test 1: Emits event before and after LLM generation with duration and tokens."""
        handler = StructuredTelemetryHandler(caller_id="test_runner")

        def mock_model(messages, info):
            return ModelResponse(parts=[TextPart("Model completed task")])

        agent = create_agent(model_fn=mock_model, telemetry_handler=handler)
        res = agent.run_sync("Run telemetry test")

        event_types = [e["event_type"] for e in handler.events]
        self.assertIn("llm_start", event_types)
        self.assertIn("llm_end", event_types)

        llm_end = next(e for e in handler.events if e["event_type"] == "llm_end")
        self.assertEqual(llm_end["status"], "success")
        self.assertGreaterEqual(llm_end["duration_ms"], 0.0)
        self.assertIn("tokens_in", llm_end)
        self.assertIn("tokens_out", llm_end)
        self.assertEqual(llm_end["caller_id"], "test_runner")

    def test_tool_telemetry_emitted(self):
        """Test 2: Emits event before and after tool execution with status and duration."""
        handler = StructuredTelemetryHandler(caller_id="tool_runner")

        def custom_tool(ctx: RunContext, query: str) -> str:
            return f"Queried: {query}"

        agent = create_agent(tool_fn=custom_tool, telemetry_handler=handler)
        result = agent._instrumented_tool(None, query="quantum")

        self.assertEqual(result, "Queried: quantum")
        event_types = [e["event_type"] for e in handler.events]
        self.assertIn("tool_start", event_types)
        self.assertIn("tool_end", event_types)

        tool_end = next(e for e in handler.events if e["event_type"] == "tool_end")
        self.assertEqual(tool_end["status"], "success")
        self.assertEqual(tool_end["tool_name"], "custom_tool")
        self.assertGreaterEqual(tool_end["duration_ms"], 0.0)

    def test_payload_passthrough_unmutated(self):
        """Test 3: Passes through execution payloads without mutating model or tool events."""
        handler = StructuredTelemetryHandler()
        expected_output = "Exact unmutated payload data 12345"

        def mock_model(messages, info):
            return ModelResponse(parts=[TextPart(expected_output)])

        agent = create_agent(model_fn=mock_model, telemetry_handler=handler)
        res = agent.run_sync("Passthrough prompt")
        self.assertEqual(res.output, expected_output)

    def test_credentials_not_leaked(self):
        """Test 4: Does not leak credentials or unmasked secrets in structured logs."""
        handler = StructuredTelemetryHandler()
        sensitive_data = {
            "prompt": "Here is my secret token: api_key='sk-live-123456789abcdef'",
            "api_key": "secret_super_token",
            "password": "my_password_999"
        }
        handler.record_event("custom_event", sensitive_data)

        recorded = handler.events[0]
        self.assertNotIn("sk-live-123456789abcdef", json.dumps(recorded))
        self.assertNotIn("secret_super_token", json.dumps(recorded))
        self.assertNotIn("my_password_999", json.dumps(recorded))
        self.assertIn("[REDACTED]", json.dumps(recorded))

    def test_tool_error_telemetry(self):
        """Test 5: Captures tool error status in telemetry."""
        handler = StructuredTelemetryHandler()

        def failing_tool(ctx: RunContext, query: str):
            raise RuntimeError("Database connection dropped")

        agent = create_agent(tool_fn=failing_tool, telemetry_handler=handler)
        with self.assertRaises(RuntimeError):
            agent._instrumented_tool(None, query="bad")

        tool_end = next(e for e in handler.events if e["event_type"] == "tool_end")
        self.assertEqual(tool_end["status"], "error")
        self.assertEqual(tool_end["error_type"], "RuntimeError")


if __name__ == "__main__":
    unittest.main()
