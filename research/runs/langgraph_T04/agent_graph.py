"""
Turn-based LangGraph agent graph implementation with model response caching.

Constructs a standard cyclical agent graph using StateGraph, MessagesState,
a cached model execution node, and a tool execution node.
"""
from typing import Any, Callable, Dict, List, Optional
from langchain_core.messages import AIMessage
from langgraph.graph import StateGraph, MessagesState, START, END
from langgraph.prebuilt import ToolNode, tools_condition

try:
    from response_cache import ResponseCache, create_cached_model_node
except ImportError:
    from research.runs.langgraph_T04.response_cache import ResponseCache, create_cached_model_node


def default_model_fn(state: MessagesState) -> Dict[str, Any]:
    """Default fallback model invocation node."""
    return {"messages": [AIMessage(content="Response from baseline model.")]}


def default_search_tool(query: str) -> str:
    """Default fallback tool implementation."""
    return f"Search results for: {query}"


def build_agent_graph(
    model_fn: Optional[Callable[[MessagesState], Dict[str, Any]]] = None,
    tools: Optional[List[Callable[..., Any]]] = None,
    cache: Optional[ResponseCache] = None,
    system_prompt: Optional[str] = None,
):
    """
    Constructs and compiles the LangGraph agent workflow with response caching.
    """
    builder = StateGraph(MessagesState)

    base_model = model_fn or default_model_fn
    if cache is not None:
        node_model = create_cached_model_node(base_model, cache, system_prompt=system_prompt)
    else:
        node_model = base_model

    builder.add_node("model", node_model)

    node_tools = tools if tools is not None else [default_search_tool]
    builder.add_node("tools", ToolNode(node_tools))

    builder.add_edge(START, "model")
    builder.add_conditional_edges("model", tools_condition)
    builder.add_edge("tools", "model")

    return builder.compile()
