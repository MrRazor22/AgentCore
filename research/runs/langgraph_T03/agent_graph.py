"""
Turn-based LangGraph agent graph implementation with HITL tool approval via interrupt().

Constructs a cyclical agent graph with StateGraph, MessagesState,
ApprovalToolNode, and compiles with a checkpointer for durable interrupt/resumption.
"""
from typing import Any, Callable, Dict, List, Optional, Set
from langchain_core.messages import AIMessage
from langgraph.graph import StateGraph, MessagesState, START, END
from langgraph.prebuilt import ToolNode, tools_condition
from langgraph.checkpoint.memory import MemorySaver

try:
    from approval_policy import ApprovalToolNode
except ImportError:
    from research.runs.langgraph_T03.approval_policy import ApprovalToolNode


def default_model_fn(state: MessagesState) -> Dict[str, Any]:
    """Default fallback model invocation node."""
    return {"messages": [AIMessage(content="Response from baseline model.")]}


def default_search_tool(query: str) -> str:
    """Default fallback tool implementation."""
    return f"Search results for: {query}"


def build_agent_graph(
    model_fn: Optional[Callable[[MessagesState], Dict[str, Any]]] = None,
    tools: Optional[List[Callable[..., Any]]] = None,
    sensitive_tools: Optional[Set[str]] = None,
    checkpointer: Optional[Any] = None,
):
    """
    Constructs and compiles the LangGraph agent workflow with HITL approval.
    """
    builder = StateGraph(MessagesState)

    node_model = model_fn or default_model_fn
    builder.add_node("model", node_model)

    node_tools = tools if tools is not None else [default_search_tool]
    builder.add_node("tools", ApprovalToolNode(node_tools, sensitive_tools=sensitive_tools))

    builder.add_edge(START, "model")
    builder.add_conditional_edges("model", tools_condition)
    builder.add_edge("tools", "model")

    # interrupt() requires a checkpointer in LangGraph
    saver = checkpointer if checkpointer is not None else MemorySaver()
    return builder.compile(checkpointer=saver)
