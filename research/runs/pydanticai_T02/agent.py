"""
PydanticAI agent with structured telemetry.
"""
import os
os.environ["PYDANTIC_AI_NO_BANNER"] = "1"
from typing import Optional, Callable
from pydantic_ai import Agent, RunContext
from pydantic_ai.models.function import FunctionModel
from pydantic_ai.messages import ModelResponse, TextPart

try:
    from telemetry import StructuredTelemetryHandler, TelemetryModelWrapper, instrument_tool
except ImportError:
    from research.runs.pydanticai_T02.telemetry import (
        StructuredTelemetryHandler,
        TelemetryModelWrapper,
        instrument_tool
    )


def default_model_fn(messages, info):
    return ModelResponse(parts=[TextPart("Telemetry response")])


def default_tool(ctx: RunContext, query: str) -> str:
    return f"Search result for {query}"


def create_agent(
    model_fn: Optional[Callable] = None,
    tool_fn: Optional[Callable] = None,
    telemetry_handler: Optional[StructuredTelemetryHandler] = None
) -> Agent:
    handler = telemetry_handler or StructuredTelemetryHandler()
    fn = model_fn or default_model_fn
    wrapped_model_fn = TelemetryModelWrapper(fn, handler)
    model = FunctionModel(wrapped_model_fn)
    agent = Agent(model)

    raw_tool = tool_fn or default_tool
    instrumented_tool_fn = instrument_tool(raw_tool, handler)
    agent.tool(instrumented_tool_fn)

    agent._telemetry_handler = handler
    agent._instrumented_tool = instrumented_tool_fn
    return agent
