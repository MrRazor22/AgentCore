"""
Tool approval delegate and policy for OpenAI Agents SDK (Task T03).
"""
import inspect
from typing import Callable, Any, Set
from agents.tool import FunctionTool
from agents.tool_context import ToolContext
from agents.run_context import RunContextWrapper


class SensitiveToolApprovalPolicy:
    """
    Manages sensitive tool detection and external approval delegates.
    """
    def __init__(
        self,
        sensitive_tool_names: Set[str] | None = None,
        approval_delegate: Callable[[str, dict[str, Any]], bool] | None = None
    ):
        self.sensitive_tool_names = sensitive_tool_names or {"execute_payment", "delete_database"}
        self.approval_delegate = approval_delegate or (lambda name, params: True)

    def is_sensitive(self, tool_name: str) -> bool:
        """Check if a tool is registered as sensitive."""
        return tool_name in self.sensitive_tool_names

    def check_approval(self, tool_name: str, arguments: dict[str, Any]) -> bool:
        """Delegate approval decision to external delegate."""
        return self.approval_delegate(tool_name, arguments)

    def make_approval_predicate(self):
        """Create an approval predicate for FunctionTool.needs_approval."""
        async def predicate(context: RunContextWrapper[Any], params: dict[str, Any], call_id: str) -> bool:
            # If tool needs approval according to policy, return True to suspend
            return True
        return predicate
