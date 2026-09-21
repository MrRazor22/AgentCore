"""
PydanticAI agent application with retry wrapper.
"""
import os
os.environ["PYDANTIC_AI_NO_BANNER"] = "1"
from typing import Optional, Callable
from pydantic_ai import Agent
from pydantic_ai.models.function import FunctionModel
from pydantic_ai.messages import ModelResponse, TextPart

try:
    from retry_policy import RetryModelWrapper
except ImportError:
    from research.runs.pydanticai_T01.retry_policy import RetryModelWrapper


def default_model_fn(messages, info):
    return ModelResponse(parts=[TextPart("Default response")])


def create_agent(model_fn: Optional[Callable] = None, max_retries: int = 3, sleep_fn: Optional[Callable] = None) -> Agent:
    fn = model_fn or default_model_fn
    kwargs = {"max_retries": max_retries}
    if sleep_fn is not None:
        kwargs["sleep_fn"] = sleep_fn
    retry_wrapped_fn = RetryModelWrapper(fn, **kwargs)
    model = FunctionModel(retry_wrapped_fn)
    agent = Agent(model)
    agent._retry_wrapper = retry_wrapped_fn
    return agent
