"""
Unit tests for OpenAI Agents SDK Tool Execution Approval (Task T03).

Acceptance Criteria:
1. Intercepts tools tagged as sensitive prior to tool invocation.
2. Executes tool normally when approval delegate returns true.
3. Aborts tool execution cleanly and returns rejection message when approval delegate returns false.
4. Does not block or prompt for non-sensitive tools.
"""
import sys
import json
import unittest
from pathlib import Path

CURRENT_DIR = Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

from agents import Runner
from agent_app import create_approval_agent, ApprovalMockModel
from approval_policy import SensitiveToolApprovalPolicy


class TestOpenAIAgentsToolApprovalT03(unittest.TestCase):
    """Test suite for OpenAI Agents SDK HITL Tool Approval."""

    def test_sensitive_tool_intercepted_prior_to_invocation(self):
        """Criterion 1: Intercepts tools tagged as sensitive prior to invocation."""
        log = []
        policy = SensitiveToolApprovalPolicy(sensitive_tool_names={"execute_payment"})
        model = ApprovalMockModel(tool_name="execute_payment")
        agent, _, log = create_approval_agent(policy=policy, model=model, execution_log=log)

        result = Runner.run_sync(agent, "Pay vendor 500 dollars")

        # Must be suspended with interruptions
        self.assertEqual(len(result.interruptions), 1)
        interruption = result.interruptions[0]
        self.assertEqual(interruption.tool_name, "execute_payment")
        # Must NOT have executed yet
        self.assertEqual(len(log), 0)

    def test_executes_normally_when_approved(self):
        """Criterion 2: Executes tool normally when approval delegate returns true."""
        log = []
        policy = SensitiveToolApprovalPolicy(
            sensitive_tool_names={"execute_payment"},
            approval_delegate=lambda name, params: True
        )
        model = ApprovalMockModel(tool_name="execute_payment", tool_args={"amount": 750, "recipient": "supplier"})
        agent, _, log = create_approval_agent(policy=policy, model=model, execution_log=log)

        # Initial run: intercepted
        result = Runner.run_sync(agent, "Pay supplier 750 dollars")
        self.assertEqual(len(result.interruptions), 1)
        approval_item = result.interruptions[0]

        # External approval delegate consult
        should_approve = policy.check_approval(approval_item.tool_name, json.loads(approval_item.raw_item.arguments))
        self.assertTrue(should_approve)

        # Resume with approve
        state = result.to_state()
        state.approve(approval_item)
        resumed_result = Runner.run_sync(agent, state)

        # Tool executed and workflow completed
        self.assertEqual(len(log), 1)
        self.assertEqual(log[0], ("execute_payment", 750, "supplier"))
        self.assertEqual(resumed_result.final_output, "Workflow completed successfully")

    def test_aborts_cleanly_when_rejected(self):
        """Criterion 3: Aborts tool execution cleanly and returns rejection message when rejected."""
        log = []
        policy = SensitiveToolApprovalPolicy(
            sensitive_tool_names={"execute_payment"},
            approval_delegate=lambda name, params: False
        )
        model = ApprovalMockModel(tool_name="execute_payment", tool_args={"amount": 99999, "recipient": "unknown_actor"})
        agent, _, log = create_approval_agent(policy=policy, model=model, execution_log=log)

        # Initial run: intercepted
        result = Runner.run_sync(agent, "Transfer 99999")
        self.assertEqual(len(result.interruptions), 1)
        approval_item = result.interruptions[0]

        # External approval delegate consult
        should_approve = policy.check_approval(approval_item.tool_name, json.loads(approval_item.raw_item.arguments))
        self.assertFalse(should_approve)

        # Resume with reject
        state = result.to_state()
        state.reject(approval_item)
        resumed_result = Runner.run_sync(agent, state)

        # Tool was NEVER executed
        self.assertEqual(len(log), 0)

        # Check tool rejection message recorded in items
        rejection_items = [
            item for item in resumed_result.new_items
            if hasattr(item, "output") and "not approved" in str(item.output).lower()
        ]
        self.assertGreater(len(rejection_items), 0)

    def test_non_sensitive_tools_do_not_block(self):
        """Criterion 4: Does not block or prompt for non-sensitive tools."""
        log = []
        policy = SensitiveToolApprovalPolicy(sensitive_tool_names={"execute_payment"})
        model = ApprovalMockModel(tool_name="query_account", tool_args={"account_id": "ACC-1234"})
        agent, _, log = create_approval_agent(policy=policy, model=model, execution_log=log)

        result = Runner.run_sync(agent, "Check account balance")

        # Zero interruptions
        self.assertEqual(len(result.interruptions), 0)
        # Executed directly
        self.assertEqual(len(log), 1)
        self.assertEqual(log[0], ("query_account", "ACC-1234"))
        self.assertEqual(result.final_output, "Workflow completed successfully")


if __name__ == "__main__":
    unittest.main()
