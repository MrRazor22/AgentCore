"""
Turn-based LangGraph agent graph implementation with context compaction node.

Constructs a cyclical agent graph using StateGraph, MessagesState,
a context compaction node that checks token count and summarizes old turns,
model execution node, and tool node.
"""
from typing import Any, Callable, Dict, List, Optional
from langchain_core.messages import AIMessage
from langgraph.graph import StateGraph, MessagesState, START, END
from langgraph.prebuilt import ToolNode, tools_condition

try:
    from context_compactor import ContextCompactor
except ImportError:
    from research.runs.langgraph_T08.context_compactor import ContextCompactor


def default_model_fn(state: MessagesState) -> Dict[str, Any]:
    """Default fallback model invocation node."""
    return {"messages": [AIMessage(content="Response from baseline model.")]}


def default_search_tool(query: str) -> str:
    """Default fallback tool implementation."""
    return f"Search results for: {query}"


def build_agent_graph(
    model_fn: Optional[Callable[[MessagesState], Dict[str, Any]]] = None,
    tools: Optional[List[Callable[..., Any]]] = None,
    compactor: Optional[ContextCompactor] = None,
):
    """
    Constructs and compiles the LangGraph agent workflow with context compaction node.
    """
    builder = StateGraph(MessagesState)

    node_model = model_fn or default_model_fn
    node_tools = tools if tools is not None else [default_search_tool]

    if compactor is not None:
        builder.add_node("compaction", compactor.compact)
        builder.add_node("model", node_model)
        builder.add_node("tools", ToolNode(node_tools))

        builder.add_edge(START, "compaction")
        builder.add_edge("compaction", "model")
        builder.add_conditional_edges("model", tools_condition)
        builder.add_edge("tools", "compaction")
    else:
        builder.add_node("model", node_model)
        builder.add_node("tools", ToolNode(node_tools))
        builder.add_edge(START, "model")
        builder.add_conditional_edges("model", tools_condition)
        builder.add_edge("tools", "model")

    return builder.compile()
