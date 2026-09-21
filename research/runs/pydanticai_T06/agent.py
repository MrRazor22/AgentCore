"""
PydanticAI agent with input validation and guardrails.
"""
import os
os.environ["PYDANTIC_AI_NO_BANNER"] = "1"
from typing import Optional, Callable
from pydantic_ai import Agent, RunContext
from pydantic_ai.models.function import FunctionModel
from pydantic_ai.messages import ModelResponse, TextPart

try:
    from guardrails import InputGuardrailPolicy, guard_tool, ValidationException
except ImportError:
    from research.runs.pydanticai_T06.guardrails import (
        InputGuardrailPolicy,
        guard_tool,
        ValidationException
    )


def default_model_fn(messages, info):
    return ModelResponse(parts=[TextPart("Valid response")])


def read_file_tool(ctx: RunContext, filepath: str) -> str:
    return f"Content of {filepath}"


def calculate_tool(ctx: RunContext, amount: int) -> str:
    return f"Processed amount: {amount}"


def create_agent(
    model_fn: Optional[Callable] = None,
    policy: Optional[InputGuardrailPolicy] = None
) -> Agent:
    active_policy = policy or InputGuardrailPolicy()
    fn = model_fn or default_model_fn

    # Guard prompt at model entrypoint
    def guarded_model_fn(messages, info):
        # Validate prompt text from incoming messages
        for msg in messages:
            if hasattr(msg, "parts"):
                for p in msg.parts:
                    if hasattr(p, "content") and isinstance(p.content, str):
                        active_policy.validate_prompt(p.content)
        return fn(messages, info)

    model = FunctionModel(guarded_model_fn)
    agent = Agent(model)

    guarded_read_file = guard_tool(active_policy)(read_file_tool)
    guarded_calculate = guard_tool(active_policy)(calculate_tool)

    agent.tool(guarded_read_file)
    agent.tool(guarded_calculate)

    agent._guardrail_policy = active_policy
    agent._read_file_tool = guarded_read_file
    agent._calculate_tool = guarded_calculate
    return agent
