"""
Token Bucket Client-Side Rate Limiter for PydanticAI.
Task: T05 - Rate Limiting (Token Bucket).
"""
import time
import threading
from typing import Callable, Optional


class RateLimitExceededException(Exception):
    """Raised when request rate exceeds configured token bucket capacity."""
    def __init__(self, message: str = "Client rate limit exceeded (bucket empty)"):
        super().__init__(message)


class TokenBucketRateLimiter:
    """
    Thread-safe Token Bucket rate limiter.
    rate: tokens per second
    capacity: burst limit (max tokens in bucket)
    """
    def __init__(self, rate: float, capacity: float, initial_tokens: Optional[float] = None):
        self.rate = float(rate)
        self.capacity = float(capacity)
        self.tokens = float(capacity if initial_tokens is None else initial_tokens)
        self.last_refill = time.monotonic()
        self._lock = threading.Lock()

    def _refill(self):
        now = time.monotonic()
        elapsed = now - self.last_refill
        self.tokens = min(self.capacity, self.tokens + elapsed * self.rate)
        self.last_refill = now

    def acquire(self, tokens: float = 1.0, blocking: bool = True, timeout: Optional[float] = None) -> bool:
        start_time = time.monotonic()
        while True:
            with self._lock:
                self._refill()
                if self.tokens >= tokens:
                    self.tokens -= tokens
                    return True

                if not blocking:
                    raise RateLimitExceededException(
                        f"Rate limit exceeded: needed {tokens}, available {self.tokens:.2f}"
                    )

                # Calculate wait time needed for required tokens
                deficit = tokens - self.tokens
                wait_time = deficit / self.rate

            if timeout is not None and (time.monotonic() - start_time + wait_time) > timeout:
                raise RateLimitExceededException("Timed out waiting for rate limit tokens")

            time.sleep(min(wait_time, 0.05))


class RateLimitedModelWrapper:
    """Wraps model execution function with TokenBucket rate limiting."""
    def __init__(self, model_fn: Callable, limiter: TokenBucketRateLimiter, blocking: bool = True):
        self.model_fn = model_fn
        self.limiter = limiter
        self.blocking = blocking

    def __call__(self, messages, info):
        self.limiter.acquire(tokens=1.0, blocking=self.blocking)
        return self.model_fn(messages, info)
