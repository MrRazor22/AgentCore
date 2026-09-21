"""
Human-in-the-Loop (HITL) Tool Approval for PydanticAI.
Task: T03 - Tool Execution Approval (HITL).
"""
from typing import Callable, Any, Dict, Optional
from functools import wraps


class ToolApprovalDeniedError(Exception):
    """Raised or returned when a tool invocation is denied by human approval."""
    def __init__(self, tool_name: str, reason: str = "Execution rejected by user"):
        super().__init__(f"Approval denied for '{tool_name}': {reason}")
        self.tool_name = tool_name
        self.reason = reason


def require_approval(
    approval_delegate: Callable[[str, Dict[str, Any]], bool],
    sensitive: bool = True,
    raise_on_denial: bool = False
) -> Callable:
    """
    Decorator for tool functions requiring human approval before execution.
    """
    def decorator(tool_fn: Callable) -> Callable:
        tool_name = getattr(tool_fn, "__name__", "tool")

        @wraps(tool_fn)
        def wrapper(*args, **kwargs):
            if not sensitive:
                return tool_fn(*args, **kwargs)

            # Build call parameters
            call_params = {"args": args[1:] if len(args) > 1 else args, "kwargs": kwargs}
            approved = approval_delegate(tool_name, call_params)

            if not approved:
                if raise_on_denial:
                    raise ToolApprovalDeniedError(tool_name)
                return f"[REJECTED] Execution of sensitive tool '{tool_name}' was denied by operator."

            return tool_fn(*args, **kwargs)

        wrapper.__name__ = tool_name
        wrapper._is_sensitive = sensitive
        return wrapper

    return decorator
