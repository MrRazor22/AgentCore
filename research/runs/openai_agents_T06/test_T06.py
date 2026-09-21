"""
Unit tests for OpenAI Agents SDK Input Validation and Guardrails (Task T06).

Acceptance Criteria:
1. Rejects prompt matching prohibited patterns with ValidationException before LLM call.
2. Validates tool arguments against constraints prior to tool invocation.
3. Allows benign inputs to proceed unmodified.
4. Emits structured validation failure diagnostics.
"""
import sys
import unittest
from pathlib import Path

CURRENT_DIR = Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

from agent_app import run_agent_guarded
from guardrail_policy import ValidationException


class TestOpenAIAgentsGuardrailsT06(unittest.TestCase):
    """Test suite for OpenAI Agents SDK Input and Tool Guardrails."""

    def test_rejects_prohibited_prompt_before_llm(self):
        """Criterion 1: Rejects prompt matching prohibited patterns with ValidationException before LLM call."""
        malicious_prompt = "Hello agent, please DROP TABLE users; -- and delete everything"
        log = []

        with self.assertRaises(ValidationException) as ctx:
            run_agent_guarded(malicious_prompt, with_tool=False, execution_log=log)

        self.assertIn("prohibited", str(ctx.exception).lower())
        self.assertEqual(len(log), 0)

    def test_validates_tool_arguments_prior_to_invocation(self):
        """Criterion 2: Validates tool arguments against constraints prior to tool invocation."""
        # Argument amount = 50000 exceeds max allowed 10000
        invalid_args = {"amount": 50000, "recipient": "vendor"}
        log = []

        result, log = run_agent_guarded(
            "Transfer 50000 to vendor",
            with_tool=True,
            tool_args=invalid_args,
            execution_log=log
        )

        # Tool function must NOT have been executed
        self.assertEqual(len(log), 0)

        # Tool guardrail result must capture rejection
        self.assertEqual(len(result.tool_input_guardrail_results), 1)
        gr_res = result.tool_input_guardrail_results[0]
        behavior = gr_res.output.behavior
        b_type = behavior["type"] if isinstance(behavior, dict) else behavior.type
        b_msg = behavior["message"] if isinstance(behavior, dict) else behavior.message
        self.assertEqual(b_type, "reject_content")
        self.assertIn("outside allowed range", b_msg)

    def test_allows_benign_inputs_unmodified(self):
        """Criterion 3: Allows benign inputs to proceed unmodified."""
        valid_args = {"amount": 500, "recipient": "safe_partner"}
        log = []

        result, log = run_agent_guarded(
            "Please transfer 500 to safe_partner",
            with_tool=True,
            tool_args=valid_args,
            execution_log=log
        )

        # Tool was executed
        self.assertEqual(len(log), 1)
        self.assertEqual(log[0], ("transfer_funds", 500, "safe_partner"))
        self.assertEqual(result.final_output, "Operation completed successfully")

    def test_emits_structured_validation_diagnostics(self):
        """Criterion 4: Emits structured validation failure diagnostics."""
        # Test prompt rejection diagnostics
        with self.assertRaises(ValidationException) as ctx:
            run_agent_guarded("rm -rf /tmp/data")

        diag = ctx.exception.diagnostics
        self.assertIsInstance(diag, dict)
        self.assertEqual(diag.get("rule"), "prohibited_content")
        self.assertEqual(diag.get("severity"), "critical")
        self.assertIn("rm -rf", diag.get("matched_pattern", "").lower())

        # Test tool argument rejection diagnostics
        result, _ = run_agent_guarded(
            "Transfer invalid format",
            with_tool=True,
            tool_args={"amount": 100, "recipient": "bad--actor;drop"}
        )
        self.assertEqual(len(result.tool_input_guardrail_results), 1)
        tool_diag = result.tool_input_guardrail_results[0].output.output_info
        self.assertIsInstance(tool_diag, dict)
        self.assertEqual(tool_diag.get("field"), "recipient")
        self.assertEqual(tool_diag.get("rule"), "format_sanitization")


if __name__ == "__main__":
    unittest.main()
