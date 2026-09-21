"""
Real Python unit tests for Task T01 (LangGraph Retry with Exponential Backoff).
"""
import sys
import time
import unittest
from pathlib import Path

# Add pilot directory to sys.path to enable direct imports
PILOT_DIR = Path(__file__).parent.resolve()
if str(PILOT_DIR) not in sys.path:
    sys.path.insert(0, str(PILOT_DIR))

from langchain_core.messages import HumanMessage, AIMessage
from agent_graph import create_agent
from retry_policy import HTTPError


class TestLangGraphRetry(unittest.TestCase):
    def test_retry_transient_http_429_success_on_attempt_3(self):
        """
        Test 1: Mock model node raises HTTP 429 on attempts 1 and 2, succeeds on attempt 3.
        Verifies that the agent retries transient errors, observes backoff, and completes turn successfully.
        """
        attempts = 0
        call_timestamps = []

        def flaky_model(state):
            nonlocal attempts
            attempts += 1
            call_timestamps.append(time.time())
            if attempts < 3:
                raise HTTPError(429, "Rate limit exceeded (Too Many Requests)")
            return {"messages": [AIMessage(content="Final turn completed successfully after retry")]}

        agent = create_agent(model_fn=flaky_model)
        result = agent.invoke({"messages": [HumanMessage(content="Start turn")]})

        # Verify attempt count
        self.assertEqual(attempts, 3, f"Expected 3 attempts, got {attempts}")

        # Verify turn completion
        last_message = result["messages"][-1]
        self.assertIsInstance(last_message, AIMessage)
        self.assertEqual(last_message.content, "Final turn completed successfully after retry")

        # Verify exponential backoff delay occurred between retries
        self.assertEqual(len(call_timestamps), 3)
        interval_1 = call_timestamps[1] - call_timestamps[0]
        interval_2 = call_timestamps[2] - call_timestamps[1]
        self.assertGreaterEqual(interval_1, 0.03, f"Interval 1 was {interval_1:.4f}s, expected >= 0.03s")
        self.assertGreaterEqual(interval_2, 0.07, f"Interval 2 was {interval_2:.4f}s, expected >= 0.07s")
        self.assertGreater(interval_2, interval_1, f"Expected interval 2 ({interval_2:.4f}s) > interval 1 ({interval_1:.4f}s)")

    def test_fatal_error_propagates_immediately(self):
        """
        Test 2: Fatal error (ValueError / HTTP 400) -> verify immediate propagation without retry.
        """
        attempts = 0

        def fatal_model(state):
            nonlocal attempts
            attempts += 1
            raise ValueError("Fatal invalid model parameter error")

        agent = create_agent(model_fn=fatal_model)

        with self.assertRaises(ValueError) as ctx:
            agent.invoke({"messages": [HumanMessage(content="Start turn")]})

        self.assertIn("Fatal invalid model parameter error", str(ctx.exception))
        self.assertEqual(attempts, 1, f"Fatal error must propagate immediately on attempt 1, got {attempts}")

        # Also verify non-transient HTTP 400 (Bad Request)
        http_attempts = 0

        def http_400_model(state):
            nonlocal http_attempts
            http_attempts += 1
            raise HTTPError(400, "Bad Request")

        agent_400 = create_agent(model_fn=http_400_model)
        with self.assertRaises(HTTPError) as ctx_400:
            agent_400.invoke({"messages": [HumanMessage(content="Start turn")]})

        self.assertEqual(ctx_400.exception.status_code, 400)
        self.assertEqual(http_attempts, 1, f"HTTP 400 must propagate immediately on attempt 1, got {http_attempts}")

    def test_exceeds_max_retries_raises_failure(self):
        """
        Test 3: Exceeds max retries -> verify failure is raised after max attempts (3).
        """
        attempts = 0

        def always_failing_model(state):
            nonlocal attempts
            attempts += 1
            raise HTTPError(503, "Service Unavailable")

        agent = create_agent(model_fn=always_failing_model)

        with self.assertRaises(HTTPError) as ctx:
            agent.invoke({"messages": [HumanMessage(content="Start turn")]})

        self.assertEqual(ctx.exception.status_code, 503)
        self.assertEqual(attempts, 3, f"Expected failure after exactly 3 attempts, got {attempts}")


if __name__ == "__main__":
    unittest.main()
