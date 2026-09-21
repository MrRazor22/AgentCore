"""
LangGraph Evolution Agent Graph
"""
from typing import Callable, List, Optional, Any
from langchain_core.messages import AIMessage, HumanMessage, ToolMessage
from langgraph.graph import StateGraph, MessagesState, START, END
from langgraph.prebuilt import ToolNode
from langgraph.types import RetryPolicy

def default_model(state: MessagesState) -> dict:
    return {"messages": [AIMessage(content="Model response")]}

def default_tool(city: str) -> str:
    """Get weather for city."""
    return f"Weather in {city}: 72F and Sunny"

def should_continue(state: MessagesState) -> str:
    messages = state["messages"]
    last_message = messages[-1]
    if hasattr(last_message, "tool_calls") and last_message.tool_calls:
        return "tools"
    return END

def create_evolution_agent(
    model_fn: Optional[Callable[[MessagesState], dict]] = None,
    tools: Optional[List[Callable]] = None,
    retry_policy: Optional[RetryPolicy] = None,
    checkpointer: Optional[Any] = None,
    guardrail_fn: Optional[Callable[[MessagesState], dict]] = None,
    summarize_fn: Optional[Callable[[MessagesState], dict]] = None,
):
    builder = StateGraph(MessagesState)

    model = model_fn or default_model
    builder.add_node("model", model, retry_policy=retry_policy)

    active_tools = tools if tools is not None else [default_tool]
    builder.add_node("tools", ToolNode(active_tools))

    if guardrail_fn:
        builder.add_node("guardrail", guardrail_fn)
        builder.add_edge(START, "guardrail")
        builder.add_edge("guardrail", "model")
    else:
        builder.add_edge(START, "model")

    builder.add_conditional_edges("model", should_continue, ["tools", END])

    if summarize_fn:
        builder.add_node("summarize", summarize_fn)
        builder.add_edge("tools", "summarize")
        builder.add_edge("summarize", "model")
    else:
        builder.add_edge("tools", "model")

    return builder.compile(checkpointer=checkpointer)
