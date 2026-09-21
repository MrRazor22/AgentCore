"""
Turn-based LangGraph agent graph implementation with durable WAL checkpointer.

Constructs a standard cyclical agent graph using StateGraph, MessagesState,
a model execution node, and compiles with a WALCheckpointer.
"""
from typing import Any, Callable, Dict, List, Optional
from langchain_core.messages import AIMessage
from langgraph.graph import StateGraph, MessagesState, START, END
from langgraph.prebuilt import ToolNode, tools_condition

try:
    from wal_checkpointer import WALCheckpointer
except ImportError:
    from research.runs.langgraph_T07.wal_checkpointer import WALCheckpointer


def default_model_fn(state: MessagesState) -> Dict[str, Any]:
    """Default fallback model invocation node."""
    return {"messages": [AIMessage(content="Response from baseline model.")]}


def default_search_tool(query: str) -> str:
    """Default fallback tool implementation."""
    return f"Search results for: {query}"


def build_agent_graph(
    model_fn: Optional[Callable[[MessagesState], Dict[str, Any]]] = None,
    tools: Optional[List[Callable[..., Any]]] = None,
    checkpointer: Optional[Any] = None,
):
    """
    Constructs and compiles the LangGraph agent workflow with WAL checkpointer.
    """
    builder = StateGraph(MessagesState)

    node_model = model_fn or default_model_fn
    builder.add_node("model", node_model)

    node_tools = tools if tools is not None else [default_search_tool]
    builder.add_node("tools", ToolNode(node_tools))

    builder.add_edge(START, "model")
    builder.add_conditional_edges("model", tools_condition)
    builder.add_edge("tools", "model")

    return builder.compile(checkpointer=checkpointer)
