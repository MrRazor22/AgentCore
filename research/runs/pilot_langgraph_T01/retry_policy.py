"""
Task T01: Retry with Exponential Backoff for LangGraph.
Configures LangGraph RetryPolicy for transient HTTP provider errors (HTTP 429 / 503).
"""
from typing import Callable
from langgraph.types import RetryPolicy

class HTTPError(Exception):
    """Represents an HTTP exception with a status code and error message."""
    def __init__(self, status_code: int, message: str = ""):
        super().__init__(f"HTTP {status_code}: {message}")
        self.status_code = status_code
        self.message = message

def is_transient_error(exc: Exception) -> bool:
    """
    Identifies transient HTTP provider errors (HTTP 429 Rate Limit, HTTP 503 Service Unavailable).
    Fatal or non-transient exceptions (e.g. ValueError, TypeError, HTTP 400) return False.
    """
    if isinstance(exc, HTTPError):
        return exc.status_code in (429, 503)
    if hasattr(exc, "status_code") and getattr(exc, "status_code") in (429, 503):
        return True
    if hasattr(exc, "response") and hasattr(exc.response, "status_code"):
        return exc.response.status_code in (429, 503)
    return False

def get_retry_policy(
    initial_interval: float = 0.05,
    backoff_factor: float = 2.0,
    max_interval: float = 1.0,
    max_attempts: int = 3,
    jitter: bool = False,
    retry_on: Callable[[Exception], bool] = is_transient_error,
) -> RetryPolicy:
    """
    Creates a LangGraph RetryPolicy instance configured for transient HTTP provider errors.
    """
    return RetryPolicy(
        initial_interval=initial_interval,
        backoff_factor=backoff_factor,
        max_interval=max_interval,
        max_attempts=max_attempts,
        jitter=jitter,
        retry_on=retry_on,
    )
