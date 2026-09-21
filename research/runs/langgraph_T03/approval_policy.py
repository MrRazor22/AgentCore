"""
Tool Execution Approval (HITL) Policy for LangGraph using interrupt().
"""
from typing import Any, Callable, Dict, List, Set
from langchain_core.messages import ToolMessage
from langgraph.types import interrupt
from langgraph.prebuilt import ToolNode

class ApprovalPolicy:
    def __init__(self, sensitive_tools: Optional[Set[str]] = None):
        self.sensitive_tools = sensitive_tools or set()

    def is_sensitive(self, tool_name: str) -> bool:
        return tool_name in self.sensitive_tools


class ApprovalToolNode:
    """
    Tool execution node that intercepts calls to sensitive tools via LangGraph's interrupt().
    """
    def __init__(self, tools: List[Callable[..., Any]], sensitive_tools: Optional[Set[str]] = None):
        self.tool_node = ToolNode(tools)
        self.sensitive_tools = set(sensitive_tools or [])
        self.tools_map = {t.__name__ if hasattr(t, "__name__") else getattr(t, "name", str(t)): t for t in tools}

    def __call__(self, state: Dict[str, Any]) -> Dict[str, Any]:
        last_msg = state.get("messages", [])[-1]
        tool_calls = getattr(last_msg, "tool_calls", [])
        if not tool_calls:
            return self.tool_node.invoke(state)

        # Check if any tool is sensitive
        tool_call = tool_calls[0]
        tool_name = tool_call.get("name")
        tool_args = tool_call.get("args")

        if tool_name in self.sensitive_tools:
            approval = interrupt({
                "action": "approval_required",
                "tool": tool_name,
                "args": tool_args,
            })
            if not approval:
                return {
                    "messages": [
                        ToolMessage(
                            content=f"Tool execution rejected by user: {tool_name}",
                            tool_call_id=tool_call.get("id", "call_rejected")
                        )
                    ]
                }

        return self.tool_node.invoke(state)
