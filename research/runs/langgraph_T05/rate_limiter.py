"""
Token Bucket Rate Limiter for LangGraph Model Invocations.
"""
import time
import threading
from typing import Any, Callable, Dict, Optional


class RateLimitExceededException(Exception):
    """Raised when rate limit is exceeded in non-blocking mode."""
    pass


class TokenBucketRateLimiter:
    def __init__(self, rate: float, burst: int):
        """
        rate: tokens added per second
        burst: maximum token capacity
        """
        self.rate = rate
        self.burst = burst
        self.tokens = float(burst)
        self.last_update = time.time()
        self.lock = threading.Lock()

    def _replenish(self):
        now = time.time()
        elapsed = now - self.last_update
        self.tokens = min(float(self.burst), self.tokens + elapsed * self.rate)
        self.last_update = now

    def acquire(self, blocking: bool = True, timeout: Optional[float] = None) -> bool:
        with self.lock:
            self._replenish()
            if self.tokens >= 1.0:
                self.tokens -= 1.0
                return True

            if not blocking:
                raise RateLimitExceededException("Rate limit burst capacity exceeded.")

            # Blocking mode
            needed = 1.0 - self.tokens
            wait_time = needed / self.rate
            if timeout is not None and wait_time > timeout:
                raise RateLimitExceededException(f"Rate limit timeout ({wait_time:.2f}s > {timeout:.2f}s)")

        # Release lock during sleep
        time.sleep(wait_time)

        with self.lock:
            self._replenish()
            self.tokens = max(0.0, self.tokens - 1.0)
            return True


def create_rate_limited_model_node(
    model_fn: Callable,
    limiter: TokenBucketRateLimiter,
    blocking: bool = True
) -> Callable:
    def rate_limited_model_node(state: Dict[str, Any]) -> Dict[str, Any]:
        limiter.acquire(blocking=blocking)
        return model_fn(state)
    return rate_limited_model_node
