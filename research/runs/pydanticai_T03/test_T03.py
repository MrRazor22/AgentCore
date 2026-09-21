"""
Unit tests for PydanticAI tool approval (Task T03).

Verifies:
1. Intercepts tools tagged as sensitive prior to tool invocation.
2. Executes tool normally when approval delegate returns true.
3. Aborts tool execution cleanly and returns rejection message when approval delegate returns false.
4. Does not block or prompt for non-sensitive tools.
"""
import sys
import unittest
from pathlib import Path

CURRENT_DIR = Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

from agent import create_agent
from approval import ToolApprovalDeniedError


class TestPydanticAIApprovalT03(unittest.TestCase):
    """Test suite for PydanticAI HITL Tool Approval."""

    def test_sensitive_tool_intercepted_and_approved(self):
        """Test 1 & 2: Sensitive tool intercepted, approved, and executed."""
        calls = []

        def delegate(tool_name: str, params: dict) -> bool:
            calls.append((tool_name, params))
            return True

        agent = create_agent(approval_delegate=delegate)
        result = agent._sql_tool(None, query="DELETE FROM users WHERE id=5")

        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0][0], "execute_sql")
        self.assertIn("DELETE FROM users", str(calls[0][1]))
        self.assertEqual(result, "SQL executed successfully: DELETE FROM users WHERE id=5")

    def test_sensitive_tool_denied_clean_rejection(self):
        """Test 3: Aborts cleanly and returns rejection message when approval returns false."""
        calls = []

        def delegate(tool_name: str, params: dict) -> bool:
            calls.append((tool_name, params))
            return False

        agent = create_agent(approval_delegate=delegate, raise_on_denial=False)
        result = agent._sql_tool(None, query="DROP TABLE accounts")

        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0][0], "execute_sql")
        self.assertIn("[REJECTED]", result)
        self.assertIn("denied by operator", result)

    def test_sensitive_tool_denied_raises_exception_if_configured(self):
        """Test 3b: Can optionally raise ToolApprovalDeniedError on rejection."""
        def rejecting_delegate(tool_name: str, params: dict) -> bool:
            return False

        agent = create_agent(approval_delegate=rejecting_delegate, raise_on_denial=True)
        with self.assertRaises(ToolApprovalDeniedError) as ctx:
            agent._sql_tool(None, query="DROP DATABASE prod")

        self.assertEqual(ctx.exception.tool_name, "execute_sql")

    def test_non_sensitive_tool_does_not_prompt(self):
        """Test 4: Non-sensitive tools execute without invoking the approval delegate."""
        calls = []

        def delegate(tool_name: str, params: dict) -> bool:
            calls.append((tool_name, params))
            return False  # If invoked, it would deny

        agent = create_agent(approval_delegate=delegate)
        result = agent._search_tool(None, query="system architecture")

        self.assertEqual(len(calls), 0)  # Delegate never called for benign tool
        self.assertEqual(result, "Documents matching 'system architecture'")


if __name__ == "__main__":
    unittest.main()
