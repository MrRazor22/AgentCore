"""
Controlled Interruption and Resumption Controller for LangGraph.
"""
from typing import Any, Callable, Dict, Optional
from langchain_core.messages import HumanMessage, AIMessage, ToolMessage
from langgraph.types import interrupt, Command


class RunController:
    def __init__(self, condition_fn: Optional[Callable[[Dict[str, Any]], bool]] = None):
        self.condition_fn = condition_fn or (lambda state: True)

    def review_node(self, state: Dict[str, Any]) -> Dict[str, Any]:
        messages = state.get("messages", [])
        last_msg = messages[-1] if messages else None
        
        if self.condition_fn(state):
            payload = {
                "interruption_type": "step_pause",
                "pending_message": last_msg.content if last_msg else None,
                "history_length": len(messages),
            }
            # Halt execution and preserve snapshot
            resume_data = interrupt(payload)
            if resume_data and isinstance(resume_data, dict) and "feedback" in resume_data:
                return {"messages": [HumanMessage(content=resume_data["feedback"])]}
        return {}
