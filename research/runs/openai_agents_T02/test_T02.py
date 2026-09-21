"""
Unit tests for OpenAI Agents SDK structured telemetry (Task T02).

Verifies:
1. Emits event before and after LLM generation with duration and token usage.
2. Emits event before and after tool execution with execution status and duration.
3. Passes through execution payloads without mutating model or tool events.
4. Does not leak credentials or unmasked secrets in structured logs.
"""
import sys
import json
import unittest
from pathlib import Path

CURRENT_DIR = Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

from agent_app import run_agent_with_telemetry
from telemetry import StructuredTelemetryRunHooks


class TestOpenAIAgentsTelemetryT02(unittest.TestCase):
    """Test suite for OpenAI Agents SDK Telemetry Hooks."""

    def test_llm_telemetry_emitted(self):
        """Test 1: Emits event before and after LLM generation with duration and tokens."""
        hooks = StructuredTelemetryRunHooks(caller_id="test_suite")
        result, _ = run_agent_with_telemetry("Test LLM telemetry", with_tool=False, hooks=hooks)

        event_types = [e["event_type"] for e in hooks.events]
        self.assertIn("llm_start", event_types)
        self.assertIn("llm_end", event_types)

        llm_end = next(e for e in hooks.events if e["event_type"] == "llm_end")
        self.assertEqual(llm_end["status"], "success")
        self.assertEqual(llm_end["caller_id"], "test_suite")
        self.assertGreaterEqual(llm_end["duration_ms"], 0.0)
        self.assertEqual(llm_end["tokens_in"], 25)
        self.assertEqual(llm_end["tokens_out"], 30)

    def test_tool_telemetry_emitted(self):
        """Test 2: Emits event before and after tool execution with status and duration."""
        hooks = StructuredTelemetryRunHooks(caller_id="tool_checker")
        result, _ = run_agent_with_telemetry("Test tool telemetry", with_tool=True, hooks=hooks)

        event_types = [e["event_type"] for e in hooks.events]
        self.assertIn("tool_start", event_types)
        self.assertIn("tool_end", event_types)

        tool_end = next(e for e in hooks.events if e["event_type"] == "tool_end")
        self.assertEqual(tool_end["status"], "success")
        self.assertEqual(tool_end["tool_name"], "search_knowledge")
        self.assertEqual(tool_end["caller_id"], "tool_checker")
        self.assertGreaterEqual(tool_end["duration_ms"], 0.0)

    def test_payload_passthrough_unmutated(self):
        """Test 3: Passes through execution payloads without mutating model or tool events."""
        result, _ = run_agent_with_telemetry("Unmutated prompt")
        self.assertEqual(result.final_output, "Telemetry execution completed")

    def test_credentials_not_leaked(self):
        """Test 4: Does not leak credentials or unmasked secrets in structured logs."""
        hooks = StructuredTelemetryRunHooks()
        hooks.record_event("test_security", {
            "prompt": "Here is API key: api_key='sk-test-super-secret-9999'",
            "secret_field": "confidential_password",
            "normal_field": "public_data"
        })

        logged_json = json.dumps(hooks.events[0])
        self.assertNotIn("sk-test-super-secret-9999", logged_json)
        self.assertNotIn("confidential_password", logged_json)
        self.assertIn("[REDACTED]", logged_json)
        self.assertIn("public_data", logged_json)


if __name__ == "__main__":
    unittest.main()
