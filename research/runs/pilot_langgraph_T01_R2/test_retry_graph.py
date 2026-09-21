"""
Unit tests for LangGraph model node retry policy (Task T01 - Replicate 2).

Verifies:
1. Transient HTTP 429/503 errors trigger exponential backoff retry and succeed.
2. Fatal non-transient errors (e.g., ValueError, HTTP 400) fail immediately without retry.
3. Exceeding max retries raises the underlying transient exception.
"""
import sys
import unittest
from pathlib import Path

# Ensure directory is on sys.path for direct module resolution
CURRENT_DIR = Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

from langchain_core.messages import AIMessage, HumanMessage
from agent_graph import build_agent_graph
from retry_policy import (
    ProviderAPIError,
    RateLimitError,
    ServiceUnavailableError,
)


class TestLangGraphRetryPolicyR2(unittest.TestCase):
    """Test suite for LangGraph RetryPolicy on model invocations."""

    def test_transient_429_retries_and_succeeds(self):
        """Test 1: Transient 429 error retries and succeeds."""
        attempt_count = 0

        def flaky_model(state):
            nonlocal attempt_count
            attempt_count += 1
            if attempt_count < 3:
                raise RateLimitError("Rate limit exceeded on provider")
            return {"messages": [AIMessage(content="Successfully processed after retry")]}

        app = build_agent_graph(model_fn=flaky_model)
        result = app.invoke({"messages": [HumanMessage(content="Hello")]})

        self.assertEqual(attempt_count, 3)
        self.assertIn("messages", result)
        self.assertEqual(result["messages"][-1].content, "Successfully processed after retry")

    def test_fatal_error_propagates_immediately(self):
        """Test 2: Fatal error (ValueError / HTTP 400) propagates immediately without retry."""
        attempt_count_val = 0

        def fatal_val_model(state):
            nonlocal attempt_count_val
            attempt_count_val += 1
            raise ValueError("Fatal invalid configuration")

        app_val = build_agent_graph(model_fn=fatal_val_model)
        with self.assertRaises(ValueError):
            app_val.invoke({"messages": [HumanMessage(content="Trigger fatal")]})
        self.assertEqual(attempt_count_val, 1)

        attempt_count_http = 0

        def fatal_http_model(state):
            nonlocal attempt_count_http
            attempt_count_http += 1
            raise ProviderAPIError(400, "Bad Request")

        app_http = build_agent_graph(model_fn=fatal_http_model)
        with self.assertRaises(ProviderAPIError):
            app_http.invoke({"messages": [HumanMessage(content="Trigger 400")]})
        self.assertEqual(attempt_count_http, 1)

    def test_exceeding_max_retries_raises_exception(self):
        """Test 3: Exceeding max retries raises the exception."""
        attempt_count = 0

        def persistent_error_model(state):
            nonlocal attempt_count
            attempt_count += 1
            raise ServiceUnavailableError("Provider 503 unavailable")

        app = build_agent_graph(model_fn=persistent_error_model)
        with self.assertRaises(ServiceUnavailableError):
            app.invoke({"messages": [HumanMessage(content="Persistent failure")]})

        self.assertEqual(attempt_count, 3)


if __name__ == "__main__":
    unittest.main()
