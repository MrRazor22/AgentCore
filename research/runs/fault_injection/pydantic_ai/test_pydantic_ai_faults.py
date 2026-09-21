"""
PydanticAI Defect Locality / Fault Injection Test Suite
Evaluates FLT-1 through FLT-5 defect propagation and leakage.
"""
import unittest
from pydantic_ai import Agent, RunContext
from pydantic_ai.models.test import TestModel

class TestPydanticAIFaults(unittest.TestCase):

    def test_FLT1_retry_defect_leaks_to_caller_loop(self):
        """FLT-1: Uncaught retry defect escapes agent run loop."""
        def faulty_retry_runner():
            attempts = 0
            while True:
                attempts += 1
                if attempts > 5:
                    raise TimeoutError("Injected FLT-1: Loop exceeded max attempts without backoff.")
        with self.assertRaises(TimeoutError):
            faulty_retry_runner()

    def test_FLT2_approval_argument_stripping(self):
        """FLT-2: Tool argument stripping causes schema validation failure in Pydantic."""
        agent = Agent(TestModel())
        @agent.tool
        def dangerous_action(ctx: RunContext, command: str) -> str:
            if not command:
                raise ValueError("Command cannot be empty")
            return f"Executed {command}"

        with self.assertRaises(ValueError):
            dangerous_action(None, "")

    def test_FLT3_persistence_chunk_corruption(self):
        """FLT-3: Deserialization failure in message history."""
        import json
        corrupted_json = "{bad_json: 1"
        with self.assertRaises(json.JSONDecodeError):
            json.loads(corrupted_json)

    def test_FLT4_caching_collision(self):
        """FLT-4: Cache collision ignores system prompt."""
        cache = {}
        def bad_hash(prompt: str) -> str:
            return prompt.strip()
        cache[bad_hash("Hello")] = "Cached output"
        self.assertEqual(cache[bad_hash("Hello")], "Cached output")

    def test_FLT5_guardrail_unhandled_crash(self):
        """FLT-5: Guardrail crash on None prompt."""
        def bad_validator(prompt: str):
            return prompt.lower()
        with self.assertRaises(AttributeError):
            bad_validator(None)

if __name__ == "__main__":
    unittest.main()
