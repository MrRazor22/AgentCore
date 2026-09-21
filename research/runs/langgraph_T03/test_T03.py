"""
Acceptance tests for Task T03: Tool Execution Approval (HITL) via interrupt().
"""
import unittest
from langchain_core.messages import HumanMessage, AIMessage, ToolMessage
from langgraph.types import Command
from langgraph.checkpoint.memory import MemorySaver

try:
    from agent_graph import build_agent_graph
except ImportError:
    from research.runs.langgraph_T03.agent_graph import build_agent_graph


def transfer_funds(account: str, amount: int) -> str:
    """Transfer funds to an account."""
    return f"Transferred ${amount} to {account}"


def search_info(query: str) -> str:
    """Search for information."""
    return f"Search info for {query}"


class TestLangGraphT03Approval(unittest.TestCase):
    def test_intercepts_sensitive_tool_prior_to_invocation(self):
        turn = 0
        def model_fn(state):
            nonlocal turn
            turn += 1
            if turn == 1:
                return {
                    "messages": [
                        AIMessage(
                            content="",
                            tool_calls=[{"name": "transfer_funds", "args": {"account": "acc_1", "amount": 500}, "id": "call_1"}]
                        )
                    ]
                }
            return {"messages": [AIMessage(content="Transfer processed")]}

        graph = build_agent_graph(
            model_fn=model_fn,
            tools=[transfer_funds, search_info],
            sensitive_tools={"transfer_funds"}
        )

        config = {"configurable": {"thread_id": "test_thread_intercept"}}
        graph.invoke({"messages": [HumanMessage(content="Transfer $500")]}, config=config)

        state = graph.get_state(config)
        self.assertIn("tools", state.next)
        self.assertTrue(len(state.tasks) > 0)
        interrupts = state.tasks[0].interrupts
        self.assertTrue(len(interrupts) > 0)
        self.assertEqual(interrupts[0].value["action"], "approval_required")
        self.assertEqual(interrupts[0].value["tool"], "transfer_funds")

    def test_executes_tool_when_approved(self):
        turn = 0
        def model_fn(state):
            nonlocal turn
            turn += 1
            if turn == 1:
                return {
                    "messages": [
                        AIMessage(
                            content="",
                            tool_calls=[{"name": "transfer_funds", "args": {"account": "acc_2", "amount": 100}, "id": "call_2"}]
                        )
                    ]
                }
            return {"messages": [AIMessage(content="Approved transfer completed successfully")]}

        graph = build_agent_graph(
            model_fn=model_fn,
            tools=[transfer_funds, search_info],
            sensitive_tools={"transfer_funds"}
        )

        config = {"configurable": {"thread_id": "test_thread_approve"}}
        graph.invoke({"messages": [HumanMessage(content="Transfer $100")]}, config=config)

        # Resume with True (approved)
        result = graph.invoke(Command(resume=True), config=config)
        final_msg = result["messages"][-1]
        self.assertEqual(final_msg.content, "Approved transfer completed successfully")
        # Check tool execution message was recorded
        tool_msgs = [m for m in result["messages"] if isinstance(m, ToolMessage)]
        self.assertEqual(len(tool_msgs), 1)
        self.assertIn("Transferred $100 to acc_2", tool_msgs[0].content)

    def test_aborts_cleanly_when_rejected(self):
        turn = 0
        def model_fn(state):
            nonlocal turn
            turn += 1
            if turn == 1:
                return {
                    "messages": [
                        AIMessage(
                            content="",
                            tool_calls=[{"name": "transfer_funds", "args": {"account": "acc_3", "amount": 1000}, "id": "call_3"}]
                        )
                    ]
                }
            return {"messages": [AIMessage(content="Transfer was rejected")]}

        graph = build_agent_graph(
            model_fn=model_fn,
            tools=[transfer_funds, search_info],
            sensitive_tools={"transfer_funds"}
        )

        config = {"configurable": {"thread_id": "test_thread_reject"}}
        graph.invoke({"messages": [HumanMessage(content="Transfer $1000")]}, config=config)

        # Resume with False (rejected)
        result = graph.invoke(Command(resume=False), config=config)
        tool_msgs = [m for m in result["messages"] if isinstance(m, ToolMessage)]
        self.assertEqual(len(tool_msgs), 1)
        self.assertIn("Tool execution rejected by user", tool_msgs[0].content)
        self.assertEqual(result["messages"][-1].content, "Transfer was rejected")

    def test_does_not_block_non_sensitive_tools(self):
        turn = 0
        def model_fn(state):
            nonlocal turn
            turn += 1
            if turn == 1:
                return {
                    "messages": [
                        AIMessage(
                            content="",
                            tool_calls=[{"name": "search_info", "args": {"query": "weather"}, "id": "call_4"}]
                        )
                    ]
                }
            return {"messages": [AIMessage(content="Weather is sunny")]}

        graph = build_agent_graph(
            model_fn=model_fn,
            tools=[transfer_funds, search_info],
            sensitive_tools={"transfer_funds"}
        )

        config = {"configurable": {"thread_id": "test_thread_non_sensitive"}}
        # Must finish in one shot without interruption
        result = graph.invoke({"messages": [HumanMessage(content="What is the weather?")]}, config=config)
        state = graph.get_state(config)
        self.assertEqual(len(state.next), 0)
        self.assertEqual(result["messages"][-1].content, "Weather is sunny")


if __name__ == "__main__":
    unittest.main()
