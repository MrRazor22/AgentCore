"""
Acceptance tests for Task T05: Rate Limiting (Token Bucket).
"""
import time
import threading
import unittest
from langchain_core.messages import HumanMessage, AIMessage

try:
    from agent_graph import build_agent_graph
    from rate_limiter import TokenBucketRateLimiter, RateLimitExceededException
except ImportError:
    from research.runs.langgraph_T05.agent_graph import build_agent_graph
    from research.runs.langgraph_T05.rate_limiter import TokenBucketRateLimiter, RateLimitExceededException


class TestLangGraphT05RateLimiting(unittest.TestCase):
    def test_enforces_burst_capacity(self):
        # 2 tokens burst capacity
        limiter = TokenBucketRateLimiter(rate=1.0, burst=2)
        graph = build_agent_graph(rate_limiter=limiter, blocking=False)

        # 1st and 2nd calls succeed immediately within burst
        res1 = graph.invoke({"messages": [HumanMessage(content="Query 1")]})
        res2 = graph.invoke({"messages": [HumanMessage(content="Query 2")]})
        self.assertIn("messages", res1)
        self.assertIn("messages", res2)

    def test_delays_requests_until_replenished(self):
        # 5 tokens/sec, burst 1
        limiter = TokenBucketRateLimiter(rate=10.0, burst=1)
        graph = build_agent_graph(rate_limiter=limiter, blocking=True)

        t0 = time.time()
        graph.invoke({"messages": [HumanMessage(content="Query 1")]})
        graph.invoke({"messages": [HumanMessage(content="Query 2")]})
        elapsed = time.time() - t0

        # Second call must have waited at least ~0.08s (1/10th sec)
        self.assertGreaterEqual(elapsed, 0.07)

    def test_throws_when_non_blocking_capacity_exceeded(self):
        limiter = TokenBucketRateLimiter(rate=0.5, burst=1)
        graph = build_agent_graph(rate_limiter=limiter, blocking=False)

        # 1st call consumes the single token
        graph.invoke({"messages": [HumanMessage(content="Query 1")]})

        # 2nd immediate call must raise RateLimitExceededException
        with self.assertRaises(RateLimitExceededException):
            graph.invoke({"messages": [HumanMessage(content="Query 2")]})

    def test_thread_safe_token_consumption_under_concurrency(self):
        burst_cap = 5
        limiter = TokenBucketRateLimiter(rate=0.1, burst=burst_cap)
        graph = build_agent_graph(rate_limiter=limiter, blocking=False)

        successes = 0
        lock = threading.Lock()

        def worker():
            nonlocal successes
            try:
                graph.invoke({"messages": [HumanMessage(content="Concurrent call")]})
                with lock:
                    successes += 1
            except RateLimitExceededException:
                pass

        threads = [threading.Thread(target=worker) for _ in range(12)]
        for t in threads:
            t.start()
        for t in threads:
            t.join()

        # Under concurrency, exactly burst_cap calls must succeed and rest fail
        self.assertEqual(successes, burst_cap)


if __name__ == "__main__":
    unittest.main()
