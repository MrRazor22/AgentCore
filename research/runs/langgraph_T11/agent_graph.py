"""
Turn-based LangGraph agent graph implementation with controlled interruption and resumption.

Constructs a cyclical agent graph using StateGraph, MessagesState,
an explicit interruption review node, model node, tool node,
and compiles with a checkpointer for durable snapshotting and resumption.
"""
from typing import Any, Callable, Dict, List, Optional
from langchain_core.messages import AIMessage
from langgraph.graph import StateGraph, MessagesState, START, END
from langgraph.prebuilt import ToolNode, tools_condition
from langgraph.checkpoint.memory import MemorySaver

try:
    from run_controller import RunController
except ImportError:
    from research.runs.langgraph_T11.run_controller import RunController


def default_model_fn(state: MessagesState) -> Dict[str, Any]:
    """Default fallback model invocation node."""
    return {"messages": [AIMessage(content="Response from baseline model.")]}


def default_search_tool(query: str) -> str:
    """Default fallback tool implementation."""
    return f"Search results for: {query}"


def build_agent_graph(
    model_fn: Optional[Callable[[MessagesState], Dict[str, Any]]] = None,
    tools: Optional[List[Callable[..., Any]]] = None,
    controller: Optional[RunController] = None,
    checkpointer: Optional[Any] = None,
):
    """
    Constructs and compiles the LangGraph agent workflow with controlled interruption.
    """
    builder = StateGraph(MessagesState)

    node_model = model_fn or default_model_fn
    builder.add_node("model", node_model)

    node_tools = tools if tools is not None else [default_search_tool]
    builder.add_node("tools", ToolNode(node_tools))

    if controller is not None:
        builder.add_node("review", controller.review_node)
        builder.add_edge(START, "model")
        builder.add_conditional_edges(
            "model",
            lambda state: "tools" if getattr(state.get("messages", [])[-1], "tool_calls", None) else "review"
        )
        builder.add_edge("tools", "model")
        builder.add_edge("review", END)
    else:
        builder.add_edge(START, "model")
        builder.add_conditional_edges("model", tools_condition)
        builder.add_edge("tools", "model")

    saver = checkpointer if checkpointer is not None else MemorySaver()
    return builder.compile(checkpointer=saver)
