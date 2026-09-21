"""
Input Validation and Guardrails for LangGraph.
"""
import re
from typing import Any, Callable, Dict, List, Optional
from langchain_core.messages import BaseMessage


class ValidationException(Exception):
    def __init__(self, message: str, diagnostics: Dict[str, Any]):
        super().__init__(message)
        self.diagnostics = diagnostics


class InputGuardrail:
    def __init__(
        self,
        prohibited_patterns: Optional[List[str]] = None,
        tool_argument_constraints: Optional[Dict[str, Dict[str, Callable[[Any], bool]]]] = None,
    ):
        self.prohibited_patterns = [re.compile(p, re.IGNORECASE) for p in (prohibited_patterns or [])]
        self.tool_constraints = tool_argument_constraints or {}

    def validate_messages(self, messages: List[BaseMessage]):
        for m in messages:
            content = str(getattr(m, "content", ""))
            for pattern in self.prohibited_patterns:
                match = pattern.search(content)
                if match:
                    raise ValidationException(
                        f"Prohibited pattern detected: {match.group(0)}",
                        diagnostics={
                            "rule": "prohibited_prompt_pattern",
                            "pattern": pattern.pattern,
                            "snippet": match.group(0),
                            "reason": "Prompt matched prohibited safety rule.",
                        }
                    )

    def validate_tool_call(self, tool_name: str, args: Dict[str, Any]):
        if tool_name in self.tool_constraints:
            constraints = self.tool_constraints[tool_name]
            for arg_name, validator in constraints.items():
                if arg_name in args:
                    val = args[arg_name]
                    if not validator(val):
                        raise ValidationException(
                            f"Tool argument validation failed for '{tool_name}.{arg_name}': {val}",
                            diagnostics={
                                "rule": "tool_argument_constraint",
                                "tool": tool_name,
                                "argument": arg_name,
                                "value": val,
                                "reason": f"Value {val} failed constraint validation.",
                            }
                        )
