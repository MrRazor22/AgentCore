"""
Input Validation and Guardrails for PydanticAI.
Task: T06 - Input Validation and Guardrails.
"""
import re
import time
from typing import Dict, Any, List, Optional, Callable
from functools import wraps


class ValidationException(Exception):
    """Raised when an input prompt or tool argument violates security guardrails."""
    def __init__(self, message: str, diagnostics: Optional[Dict[str, Any]] = None):
        super().__init__(message)
        self.message = message
        self.diagnostics = diagnostics or {}


DEFAULT_PROHIBITED_PATTERNS = [
    re.compile(r"(?i)ignore\s+(all\s+)?previous\s+instructions"),
    re.compile(r"(?i)system\s+override"),
    re.compile(r"(?i)bypass\s+(all\s+)?security"),
    re.compile(r"(?i)eval\s*\("),
    re.compile(r"(?i)<script[\s>]"),
]


class InputGuardrailPolicy:
    """Security policy for validating prompt strings and tool arguments."""
    def __init__(self, patterns: Optional[List[re.Pattern]] = None):
        self.patterns = patterns or DEFAULT_PROHIBITED_PATTERNS

    def validate_prompt(self, prompt: str) -> None:
        if not prompt or not isinstance(prompt, str):
            return
        for pattern in self.patterns:
            match = pattern.search(prompt)
            if match:
                diagnostics = {
                    "rule": "prohibited_prompt_pattern",
                    "matched_pattern": pattern.pattern,
                    "matched_text": match.group(0),
                    "timestamp": time.time()
                }
                raise ValidationException(
                    f"Prompt validation failed: prohibited pattern '{pattern.pattern}' detected.",
                    diagnostics=diagnostics
                )

    def validate_tool_args(self, tool_name: str, args: Dict[str, Any]) -> None:
        for k, v in args.items():
            if isinstance(v, str):
                # Check path traversal
                if any(bad in v.lower() for bad in ("../", "..\\", "/etc/passwd", "system32")):
                    diagnostics = {
                        "rule": "path_traversal",
                        "tool_name": tool_name,
                        "param": k,
                        "value": v,
                        "timestamp": time.time()
                    }
                    raise ValidationException(
                        f"Tool argument validation failed on '{tool_name}.{k}': illegal path traversal.",
                        diagnostics=diagnostics
                    )
            elif isinstance(v, (int, float)):
                # Check bounds
                if v < 0 or v > 1_000_000:
                    diagnostics = {
                        "rule": "range_violation",
                        "tool_name": tool_name,
                        "param": k,
                        "value": v,
                        "timestamp": time.time()
                    }
                    raise ValidationException(
                        f"Tool argument validation failed on '{tool_name}.{k}': value out of bounds [0, 1000000].",
                        diagnostics=diagnostics
                    )


def guard_tool(policy: InputGuardrailPolicy) -> Callable:
    """Decorator to enforce tool argument guardrails."""
    def decorator(tool_fn: Callable) -> Callable:
        tool_name = getattr(tool_fn, "__name__", "tool")

        @wraps(tool_fn)
        def wrapper(*args, **kwargs):
            # Validate kwargs and positional args
            policy.validate_tool_args(tool_name, kwargs)
            return tool_fn(*args, **kwargs)

        wrapper.__name__ = tool_name
        return wrapper

    return decorator
