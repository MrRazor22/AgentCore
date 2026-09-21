"""
Unit tests for PydanticAI model retry policy (Task T01).

Verifies:
1. Transient HTTP 429/503 errors trigger exponential backoff retry and succeed.
2. Fatal non-transient errors (e.g., ValueError, HTTP 400) fail immediately without retry.
3. Exceeding max retries raises the underlying transient exception.
4. Exponential backoff calculation matches expected values.
"""
import sys
import unittest
from pathlib import Path

# Ensure directory is on sys.path for direct module resolution
CURRENT_DIR = Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

from pydantic_ai.messages import ModelResponse, TextPart
from agent import create_agent
from retry_policy import (
    RateLimitError,
    ServiceUnavailableError,
    ProviderAPIError,
    calculate_backoff,
)


class TestPydanticAIRetryT01(unittest.TestCase):
    """Test suite for PydanticAI retry wrapper."""

    def test_transient_429_retries_and_succeeds(self):
        """Test 1: Transient 429 error retries and succeeds."""
        attempt_count = 0
        delays = []

        def flaky_model(messages, info):
            nonlocal attempt_count
            attempt_count += 1
            if attempt_count < 3:
                raise RateLimitError("Rate limit exceeded")
            return ModelResponse(parts=[TextPart("Success after 429 retries")])

        agent = create_agent(model_fn=flaky_model, max_retries=3, sleep_fn=lambda d: delays.append(d))
        result = agent.run_sync("Hello")

        self.assertEqual(attempt_count, 3)
        self.assertEqual(result.output, "Success after 429 retries")
        self.assertEqual(len(delays), 2)
        self.assertAlmostEqual(delays[0], 0.05, places=3)
        self.assertAlmostEqual(delays[1], 0.10, places=3)

    def test_transient_503_retries_and_succeeds(self):
        """Test 2: Transient 503 error retries and succeeds."""
        attempt_count = 0
        delays = []

        def flaky_model(messages, info):
            nonlocal attempt_count
            attempt_count += 1
            if attempt_count < 2:
                raise ServiceUnavailableError("503 Service Unavailable")
            return ModelResponse(parts=[TextPart("Success after 503 retry")])

        agent = create_agent(model_fn=flaky_model, max_retries=3, sleep_fn=lambda d: delays.append(d))
        result = agent.run_sync("Ping")

        self.assertEqual(attempt_count, 2)
        self.assertEqual(result.output, "Success after 503 retry")
        self.assertEqual(len(delays), 1)
        self.assertAlmostEqual(delays[0], 0.05, places=3)

    def test_fatal_error_propagates_immediately(self):
        """Test 3: Fatal errors propagate immediately without retry."""
        attempt_count_val = 0

        def fatal_val_model(messages, info):
            nonlocal attempt_count_val
            attempt_count_val += 1
            raise ValueError("Fatal configuration error")

        agent_val = create_agent(model_fn=fatal_val_model, max_retries=3)
        with self.assertRaises(ValueError):
            agent_val.run_sync("Test fatal val")
        self.assertEqual(attempt_count_val, 1)

        attempt_count_http = 0

        def fatal_http_model(messages, info):
            nonlocal attempt_count_http
            attempt_count_http += 1
            raise ProviderAPIError(400, "Bad Request")

        agent_http = create_agent(model_fn=fatal_http_model, max_retries=3)
        with self.assertRaises(ProviderAPIError):
            agent_http.run_sync("Test 400")
        self.assertEqual(attempt_count_http, 1)

    def test_exceeding_max_retries_raises_exception(self):
        """Test 4: Exceeding max retries raises the transient exception."""
        attempt_count = 0

        def persistent_error_model(messages, info):
            nonlocal attempt_count
            attempt_count += 1
            raise RateLimitError("Persistent rate limit")

        agent = create_agent(model_fn=persistent_error_model, max_retries=3, sleep_fn=lambda d: None)
        with self.assertRaises(RateLimitError):
            agent.run_sync("Test max retries")
        self.assertEqual(attempt_count, 3)

    def test_exponential_backoff_calculation(self):
        """Test 5: Exponential backoff calculation matches formula."""
        self.assertAlmostEqual(calculate_backoff(1, base_delay=0.1), 0.1)
        self.assertAlmostEqual(calculate_backoff(2, base_delay=0.1), 0.2)
        self.assertAlmostEqual(calculate_backoff(3, base_delay=0.1), 0.4)
        self.assertAlmostEqual(calculate_backoff(4, base_delay=0.1), 0.8)
        self.assertAlmostEqual(calculate_backoff(10, base_delay=0.1, max_delay=5.0), 5.0)


if __name__ == "__main__":
    unittest.main()
