"""
Unit tests for OpenAI Agents SDK model retry policy (Task T01).

Verifies:
1. Transient HTTP 429/503 errors trigger exponential backoff retry and succeed.
2. Fatal non-transient errors (e.g., ValueError, HTTP 400) fail immediately without retry.
3. Exceeding max retries raises the underlying transient exception.
4. Exponential backoff calculation matches expected formula.
"""
import sys
import unittest
from pathlib import Path

CURRENT_DIR = Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

from agents import Runner
from agents.models.interface import Model, ModelResponse
from openai.types.responses.response_output_message import ResponseOutputMessage, ResponseOutputText
from agents.usage import Usage

from agent_app import create_agent
from retry_policy import (
    RateLimitError,
    ServiceUnavailableError,
    ProviderAPIError,
    calculate_backoff,
)


class FlakyMockModel(Model):
    """Mock model that fails a specified number of times with an exception before succeeding."""
    def __init__(self, failure_count: int, exc_factory, success_text: str = "Success"):
        self.failure_count = failure_count
        self.exc_factory = exc_factory
        self.success_text = success_text
        self.attempts = 0

    async def get_response(self, *args, **kwargs):
        self.attempts += 1
        if self.attempts <= self.failure_count:
            raise self.exc_factory()
        msg = ResponseOutputMessage(
            id="msg_succ",
            content=[ResponseOutputText(annotations=[], text=self.success_text, type="output_text")],
            role="assistant",
            status="completed",
            type="message"
        )
        return ModelResponse(output=[msg], usage=Usage(), response_id="resp_succ", request_id="req_succ")

    async def stream_response(self, *args, **kwargs):
        raise NotImplementedError()


class TestOpenAIAgentsRetryT01(unittest.TestCase):
    """Test suite for OpenAI Agents SDK Retry Policy."""

    def test_transient_429_retries_and_succeeds(self):
        """Test 1: Transient HTTP 429 retries and succeeds."""
        delays = []
        raw_model = FlakyMockModel(2, lambda: RateLimitError("429 Too Many Requests"), "Succeeded on attempt 3")
        agent = create_agent(model=raw_model, max_retries=3, sleep_fn=lambda d: delays.append(d))

        result = Runner.run_sync(agent, "Hello")

        self.assertEqual(raw_model.attempts, 3)
        self.assertEqual(result.final_output, "Succeeded on attempt 3")
        self.assertEqual(len(delays), 2)
        self.assertAlmostEqual(delays[0], 0.05, places=3)
        self.assertAlmostEqual(delays[1], 0.10, places=3)

    def test_transient_503_retries_and_succeeds(self):
        """Test 2: Transient HTTP 503 retries and succeeds."""
        delays = []
        raw_model = FlakyMockModel(1, lambda: ServiceUnavailableError("503 Service Unavailable"), "Succeeded after 503")
        agent = create_agent(model=raw_model, max_retries=3, sleep_fn=lambda d: delays.append(d))

        result = Runner.run_sync(agent, "Ping")

        self.assertEqual(raw_model.attempts, 2)
        self.assertEqual(result.final_output, "Succeeded after 503")
        self.assertEqual(len(delays), 1)
        self.assertAlmostEqual(delays[0], 0.05, places=3)

    def test_fatal_error_propagates_immediately(self):
        """Test 3: Fatal errors propagate immediately without retry."""
        raw_model_val = FlakyMockModel(1, lambda: ValueError("Fatal argument error"))
        agent_val = create_agent(model=raw_model_val, max_retries=3)

        with self.assertRaises(ValueError):
            Runner.run_sync(agent_val, "Test fatal")
        self.assertEqual(raw_model_val.attempts, 1)

        raw_model_http = FlakyMockModel(1, lambda: ProviderAPIError(400, "Bad Request"))
        agent_http = create_agent(model=raw_model_http, max_retries=3)

        with self.assertRaises(ProviderAPIError):
            Runner.run_sync(agent_http, "Test 400")
        self.assertEqual(raw_model_http.attempts, 1)

    def test_exceeding_max_retries_raises_exception(self):
        """Test 4: Exceeding max retries raises the transient exception."""
        raw_model = FlakyMockModel(10, lambda: RateLimitError("Persistent rate limit"))
        agent = create_agent(model=raw_model, max_retries=3, sleep_fn=lambda d: None)

        with self.assertRaises(RateLimitError):
            Runner.run_sync(agent, "Persistent fail")
        self.assertEqual(raw_model.attempts, 3)

    def test_exponential_backoff_calculation(self):
        """Test 5: Exponential backoff calculation matches expected formula."""
        self.assertAlmostEqual(calculate_backoff(1, base_delay=0.1), 0.1)
        self.assertAlmostEqual(calculate_backoff(2, base_delay=0.1), 0.2)
        self.assertAlmostEqual(calculate_backoff(3, base_delay=0.1), 0.4)
        self.assertAlmostEqual(calculate_backoff(4, base_delay=0.1), 0.8)
        self.assertAlmostEqual(calculate_backoff(8, base_delay=0.1, max_delay=3.0), 3.0)


if __name__ == "__main__":
    unittest.main()
