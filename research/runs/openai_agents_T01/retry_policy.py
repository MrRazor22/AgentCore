"""
Retry policy and wrapper for OpenAI Agents SDK models.
Task: T01 - Retry with Exponential Backoff.
"""
import time
from typing import Tuple, List, Optional
from agents.models.interface import Model, ModelResponse


class ProviderAPIError(Exception):
    """Base API exception."""
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


def calculate_backoff(attempt: int, base_delay: float = 0.05, max_delay: float = 10.0) -> float:
    """Calculate exponential backoff delay: base_delay * 2^(attempt - 1)."""
    delay = base_delay * (2 ** (attempt - 1))
    return min(delay, max_delay)


class RetryModelWrapper(Model):
    """
    Wraps an underlying OpenAI Agents SDK Model with exponential backoff retry
    for transient errors (HTTP 429/503).
    """
    def __init__(
        self,
        base_model: Model,
        max_retries: int = 3,
        base_delay: float = 0.05,
        retryable_exceptions: Tuple = (RateLimitError, ServiceUnavailableError),
        sleep_fn=time.sleep
    ):
        self.base_model = base_model
        self.max_retries = max_retries
        self.base_delay = base_delay
        self.retryable_exceptions = retryable_exceptions
        self.sleep_fn = sleep_fn
        self.delays_recorded: List[float] = []

    async def get_response(
        self,
        system_instructions,
        input,
        model_settings,
        tools,
        output_schema,
        handoffs,
        tracing,
        *,
        previous_response_id=None,
        conversation_id=None,
        prompt=None
    ) -> ModelResponse:
        attempt = 0
        while True:
            attempt += 1
            try:
                return await self.base_model.get_response(
                    system_instructions,
                    input,
                    model_settings,
                    tools,
                    output_schema,
                    handoffs,
                    tracing,
                    previous_response_id=previous_response_id,
                    conversation_id=conversation_id,
                    prompt=prompt
                )
            except self.retryable_exceptions as exc:
                if attempt >= self.max_retries:
                    raise exc
                delay = calculate_backoff(attempt, self.base_delay)
                self.delays_recorded.append(delay)
                self.sleep_fn(delay)
            except Exception:
                # Fatal errors propagate immediately without retry
                raise

    async def stream_response(self, *args, **kwargs):
        raise NotImplementedError()
