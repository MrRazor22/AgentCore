"""
Retry policy and wrapper for PydanticAI models.
Task: T01 - Retry with Exponential Backoff.
"""
import time
from typing import Optional, List, Tuple
from pydantic_ai.models import Model, ModelResponse
from pydantic_ai.messages import ModelMessage, ModelResponse as PydanticModelResponse


class ProviderAPIError(Exception):
    """Base API error."""
    def __init__(self, status_code: int, message: str):
        super().__init__(f"HTTP {status_code}: {message}")
        self.status_code = status_code
        self.message = message


class RateLimitError(ProviderAPIError):
    """HTTP 429 Rate limit error."""
    def __init__(self, message: str = "Rate limit exceeded"):
        super().__init__(429, message)


class ServiceUnavailableError(ProviderAPIError):
    """HTTP 503 Service unavailable error."""
    def __init__(self, message: str = "Service unavailable"):
        super().__init__(503, message)


def calculate_backoff(attempt: int, base_delay: float = 0.1, max_delay: float = 10.0) -> float:
    """Calculate exponential backoff delay: base_delay * 2^(attempt - 1)."""
    delay = base_delay * (2 ** (attempt - 1))
    return min(delay, max_delay)


class RetryModelWrapper:
    """
    Wraps an underlying PydanticAI model callable or model instance
    with exponential backoff retry for transient errors.
    """
    def __init__(
        self,
        model_fn,
        max_retries: int = 3,
        base_delay: float = 0.05,
        retryable_exceptions: Tuple = (RateLimitError, ServiceUnavailableError),
        sleep_fn=time.sleep
    ):
        self.model_fn = model_fn
        self.max_retries = max_retries
        self.base_delay = base_delay
        self.retryable_exceptions = retryable_exceptions
        self.sleep_fn = sleep_fn
        self.delays_recorded: List[float] = []

    def __call__(self, messages, info):
        attempt = 0
        while True:
            attempt += 1
            try:
                return self.model_fn(messages, info)
            except self.retryable_exceptions as exc:
                if attempt >= self.max_retries:
                    raise exc
                delay = calculate_backoff(attempt, self.base_delay)
                self.delays_recorded.append(delay)
                self.sleep_fn(delay)
            except Exception:
                # Fatal or non-retryable exception: propagate immediately
                raise
