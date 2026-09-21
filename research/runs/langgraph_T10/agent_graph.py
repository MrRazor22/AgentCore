"""
Turn-based LangGraph agent graph implementation with real-time stream observation.

Constructs a cyclical agent graph using StateGraph, MessagesState,
a model node equipped with StreamWriter for fine-grained token/tool deltas,
and a tool execution node.
"""
from typing import Any, Callable, Dict, List, Optional
from langchain_core.messages import AIMessage
from langgraph.graph import StateGraph, MessagesState, START, END
from langgraph.prebuilt import ToolNode, tools_condition
from langgraph.types import StreamWriter

try:
    from stream_observer import StreamObserver
except ImportError:
    from research.runs.langgraph_T10.stream_observer import StreamObserver


def default_streaming_model_fn(state: MessagesState, writer: StreamWriter) -> Dict[str, Any]:
    """Default fallback streaming model node using StreamWriter."""
    writer({"type": "token_chunk", "content": "Response "})
    writer({"type": "token_chunk", "content": "from "})
    writer({"type": "token_chunk", "content": "baseline "})
    writer({"type": "token_chunk", "content": "model."})
    return {"messages": [AIMessage(content="Response from baseline model.")]}


def default_search_tool(query: str) -> str:
    """Default fallback tool implementation."""
    return f"Search results for: {query}"


def build_agent_graph(
    model_fn: Optional[Callable[..., Dict[str, Any]]] = None,
    tools: Optional[List[Callable[..., Any]]] = None,
):
    """
    Constructs and compiles the LangGraph agent workflow with real-time stream support.
    """
    builder = StateGraph(MessagesState)

    node_model = model_fn or default_streaming_model_fn
    builder.add_node("model", node_model)

    node_tools = tools if tools is not None else [default_search_tool]
    builder.add_node("tools", ToolNode(node_tools))

    builder.add_edge(START, "model")
    builder.add_conditional_edges("model", tools_condition)
    builder.add_edge("tools", "model")

    return builder.compile()
