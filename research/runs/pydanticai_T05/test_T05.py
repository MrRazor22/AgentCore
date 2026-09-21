"""
Unit tests for PydanticAI rate limiter (Task T05).

Verifies:
1. Enforces maximum call rate per second and burst capacity.
2. Delays or throttles request until token capacity replenishes.
3. Throws RateLimitExceededException when non-blocking capacity exceeded.
4. Thread-safe token consumption under concurrent requests.
"""
import sys
import time
import unittest
import threading
from pathlib import Path

CURRENT_DIR = Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

from pydantic_ai.messages import ModelResponse, TextPart
from agent import create_agent
from rate_limiter import TokenBucketRateLimiter, RateLimitExceededException


class TestPydanticAIRateLimiterT05(unittest.TestCase):
    """Test suite for PydanticAI TokenBucket Rate Limiter."""

    def test_burst_capacity_allowed(self):
        """Test 1: Requests up to burst capacity succeed immediately."""
        call_count = 0

        def counting_model(messages, info):
            nonlocal call_count
            call_count += 1
            return ModelResponse(parts=[TextPart(f"Call {call_count}")])

        limiter = TokenBucketRateLimiter(rate=10.0, capacity=3.0, initial_tokens=3.0)
        agent = create_agent(model_fn=counting_model, limiter=limiter, blocking=False)

        # 3 calls should succeed immediately
        res1 = agent.run_sync("msg 1")
        res2 = agent.run_sync("msg 2")
        res3 = agent.run_sync("msg 3")

        self.assertEqual(call_count, 3)
        self.assertEqual(res3.output, "Call 3")

    def test_non_blocking_throws_rate_limit_exceeded(self):
        """Test 2 & 3: Throws RateLimitExceededException when capacity exceeded in non-blocking mode."""
        limiter = TokenBucketRateLimiter(rate=5.0, capacity=2.0, initial_tokens=2.0)
        agent = create_agent(limiter=limiter, blocking=False)

        agent.run_sync("call 1")
        agent.run_sync("call 2")

        # 3rd call immediately must raise RateLimitExceededException
        with self.assertRaises(RateLimitExceededException):
            agent.run_sync("call 3")

    def test_blocking_delays_and_replenishes(self):
        """Test 4: Delays until token replenishes when blocking=True."""
        # 10 tokens/sec = 1 token every 100ms. Capacity=1.
        limiter = TokenBucketRateLimiter(rate=20.0, capacity=1.0, initial_tokens=1.0)
        agent = create_agent(limiter=limiter, blocking=True)

        start = time.perf_counter()
        agent.run_sync("call 1")  # uses token
        agent.run_sync("call 2")  # waits ~50ms
        elapsed = time.perf_counter() - start

        self.assertGreaterEqual(elapsed, 0.04)

    def test_thread_safe_concurrent_requests(self):
        """Test 5: Thread-safe token consumption under concurrent requests."""
        total_tokens = 5.0
        limiter = TokenBucketRateLimiter(rate=1.0, capacity=total_tokens, initial_tokens=total_tokens)
        agent = create_agent(limiter=limiter, blocking=False)

        successes = []
        failures = []

        def worker():
            try:
                agent.run_sync("concurrent prompt")
                successes.append(1)
            except RateLimitExceededException:
                failures.append(1)

        threads = [threading.Thread(target=worker) for _ in range(10)]
        for t in threads:
            t.start()
        for t in threads:
            t.join()

        # Exactly total_tokens (5) should succeed, 5 should fail with RateLimitExceededException
        self.assertEqual(len(successes), int(total_tokens))
        self.assertEqual(len(failures), 5)


if __name__ == "__main__":
    unittest.main()
