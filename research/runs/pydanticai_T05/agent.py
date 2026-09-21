"""
PydanticAI agent with TokenBucket rate limiting.
"""
import os
os.environ["PYDANTIC_AI_NO_BANNER"] = "1"
from typing import Optional, Callable
from pydantic_ai import Agent
from pydantic_ai.models.function import FunctionModel
from pydantic_ai.messages import ModelResponse, TextPart

try:
    from rate_limiter import TokenBucketRateLimiter, RateLimitedModelWrapper
except ImportError:
    from research.runs.pydanticai_T05.rate_limiter import (
        TokenBucketRateLimiter,
        RateLimitedModelWrapper
    )


def default_model_fn(messages, info):
    return ModelResponse(parts=[TextPart("Rate limited response")])


def create_agent(
    model_fn: Optional[Callable] = None,
    rate: float = 10.0,
    capacity: float = 2.0,
    blocking: bool = True,
    limiter: Optional[TokenBucketRateLimiter] = None
) -> Agent:
    bucket = limiter or TokenBucketRateLimiter(rate=rate, capacity=capacity)
    fn = model_fn or default_model_fn
    wrapped_model_fn = RateLimitedModelWrapper(fn, bucket, blocking=blocking)
    model = FunctionModel(wrapped_model_fn)
    agent = Agent(model)
    agent._rate_limiter = bucket
    return agent
