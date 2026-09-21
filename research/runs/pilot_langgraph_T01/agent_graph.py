"""
Turn-based LangGraph agent with StateGraph, MessagesState, model node with retry policy, and tool node.
"""
from typing import Callable, List, Optional
from langchain_core.messages import AIMessage
from langgraph.graph import StateGraph, MessagesState, START, END
from langgraph.prebuilt import ToolNode
from retry_policy import get_retry_policy

def default_model(state: MessagesState) -> dict:
    return {"messages": [AIMessage(content="Model response")]}

def default_tool(query: str) -> str:
    """Tool invocation handler."""
    return f"Result for {query}"

def should_continue(state: MessagesState) -> str:
    messages = state["messages"]
    last_message = messages[-1]
    if hasattr(last_message, "tool_calls") and last_message.tool_calls:
        return "tools"
    return END

def create_agent(
    model_fn: Optional[Callable[[MessagesState], dict]] = None,
    tools: Optional[List[Callable]] = None,
):
    builder = StateGraph(MessagesState)
    model = model_fn or default_model
    builder.add_node("model", model, retry_policy=get_retry_policy())

    active_tools = tools if tools is not None else [default_tool]
    builder.add_node("tools", ToolNode(active_tools))

    builder.add_edge(START, "model")
    builder.add_conditional_edges("model", should_continue, ["tools", END])
    builder.add_edge("tools", "model")

    return builder.compile()
