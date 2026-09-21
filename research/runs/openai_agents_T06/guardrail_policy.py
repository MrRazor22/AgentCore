"""
Input and tool guardrails policy for OpenAI Agents SDK (Task T06).
"""
import re
import json
from typing import Any, Dict, List
from agents import (
    input_guardrail,
    tool_input_guardrail,
    GuardrailFunctionOutput,
    ToolGuardrailFunctionOutput,
    ToolInputGuardrailData,
)


class ValidationException(Exception):
    """Raised when prompt or input violates safety guardrails."""
    def __init__(self, message: str, diagnostics: Dict[str, Any] | None = None):
        super().__init__(message)
        self.message = message
        self.diagnostics = diagnostics or {}


PROHIBITED_PATTERNS = [
    re.compile(r"(?i)\b(drop\s+table|delete\s+from|rm\s+-rf|shutdown)\b"),
    re.compile(r"(?i)\b(ignore\s+all\s+previous\s+instructions|jailbreak)\b")
]


def check_prompt_safety(text: str) -> Dict[str, Any] | None:
    """Validate prompt against prohibited rules. Returns failure info or None."""
    for pattern in PROHIBITED_PATTERNS:
        match = pattern.search(text)
        if match:
            return {
                "rule": "prohibited_content",
                "matched_pattern": match.group(0),
                "severity": "critical"
            }
    return None


@input_guardrail
def prompt_safety_guardrail(context, agent, input_data) -> GuardrailFunctionOutput:
    """Agent input guardrail executing prior to LLM generation."""
    prompt_str = input_data if isinstance(input_data, str) else str(input_data)
    violation = check_prompt_safety(prompt_str)
    if violation:
        return GuardrailFunctionOutput(
            output_info=violation,
            tripwire_triggered=True
        )
    return GuardrailFunctionOutput(output_info={"status": "passed"}, tripwire_triggered=False)


@tool_input_guardrail
def tool_arguments_guardrail(data: ToolInputGuardrailData) -> ToolGuardrailFunctionOutput:
    """Tool input guardrail validating tool arguments prior to dispatch."""
    raw_args = data.context.tool_arguments
    try:
        args = json.loads(raw_args) if isinstance(raw_args, str) else raw_args
    except Exception:
        return ToolGuardrailFunctionOutput.raise_exception(
            output_info={"error": "invalid_json_arguments", "raw": raw_args}
        )

    if not isinstance(args, dict):
        return ToolGuardrailFunctionOutput.allow(output_info={"status": "passed"})

    # Check numerical bounds (e.g. amount must be > 0 and <= 10000)
    if "amount" in args:
        amount = args["amount"]
        if not isinstance(amount, (int, float)) or amount <= 0 or amount > 10000:
            return ToolGuardrailFunctionOutput.reject_content(
                message=f"Validation failed: amount {amount} outside allowed range (0, 10000]",
                output_info={"field": "amount", "value": amount, "rule": "range_bounds", "bounds": [1, 10000]}
            )

    # Check recipient format (disallow dangerous script / sql injection tags)
    if "recipient" in args:
        rec = str(args["recipient"])
        if any(c in rec for c in [";", "--", "<script>", "/"]):
            return ToolGuardrailFunctionOutput.reject_content(
                message=f"Validation failed: recipient '{rec}' contains invalid characters",
                output_info={"field": "recipient", "value": rec, "rule": "format_sanitization"}
            )

    return ToolGuardrailFunctionOutput.allow(output_info={"status": "passed"})
