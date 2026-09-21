"""
Retry policy configuration for transient provider errors in LangGraph.

Handles transient HTTP errors (429 Too Many Requests, 503 Service Unavailable)
using LangGraph's RetryPolicy with exponential backoff up to 3 attempts.
"""
from typing import Any, Optional, Set
from langgraph.types import RetryPolicy


class ProviderAPIError(Exception):
    """Base exception for provider API responses."""

    def __init__(self, status_code: int, message: str = "") -> None:
        detail = f": {message}" if message else ""
        super().__init__(f"HTTP {status_code}{detail}")
        self.status_code = status_code


class RateLimitError(ProviderAPIError):
    """Exception representing HTTP 429 Too Many Requests."""

    def __init__(self, message: str = "Rate limit exceeded") -> None:
        super().__init__(status_code=429, message=message)


class ServiceUnavailableError(ProviderAPIError):
    """Exception representing HTTP 503 Service Unavailable."""

    def __init__(self, message: str = "Service unavailable") -> None:
        super().__init__(status_code=503, message=message)


TRANSIENT_STATUS_CODES: Set[int] = {429, 503}


def is_transient_error(exc: Exception) -> bool:
    """
    Check if an exception is a transient provider error (HTTP 429 or 503).
    Non-transient errors (e.g. ValueError, 400 Bad Request) return False.
    """
    if isinstance(exc, (RateLimitError, ServiceUnavailableError)):
        return True

    status = getattr(exc, "status_code", None)
    if status is None:
        status = getattr(exc, "code", None)

    return status in TRANSIENT_STATUS_CODES


def create_model_retry_policy(
    initial_interval: float = 0.05,
    backoff_factor: float = 2.0,
    max_attempts: int = 3,
    jitter: bool = False,
) -> RetryPolicy:
    """
    Build a LangGraph RetryPolicy for the model node.
    Retries up to max_attempts (default 3) with exponential backoff
    specifically filtering on transient HTTP 429/503 errors.
    """
    return RetryPolicy(
        initial_interval=initial_interval,
        backoff_factor=backoff_factor,
        max_attempts=max_attempts,
        jitter=jitter,
        retry_on=is_transient_error,
    )


def get_retry_policy() -> RetryPolicy:
    """Default retry policy accessor."""
    return create_model_retry_policy()
