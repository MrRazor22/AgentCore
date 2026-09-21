"""
Acceptance tests for Task T12: Dynamic Tool Discovery Evolution.
"""
import unittest
from langchain_core.messages import HumanMessage, AIMessage, ToolMessage

try:
    from agent_graph import build_agent_graph
    from dynamic_toolbox import DynamicToolRegistry
except ImportError:
    from research.runs.langgraph_T12.agent_graph import build_agent_graph
    from research.runs.langgraph_T12.dynamic_toolbox import DynamicToolRegistry


def public_search(query: str) -> str:
    """Public search utility."""
    return f"Public result for: {query}"


def admin_delete(item_id: str) -> str:
    """Admin deletion tool."""
    return f"Deleted item {item_id}"


class TestLangGraphT12DynamicTools(unittest.TestCase):
    def setUp(self):
        self.registry = DynamicToolRegistry()
        self.registry.register_tool(public_search, allowed_roles={"guest", "admin"})
        self.registry.register_tool(admin_delete, allowed_roles={"admin"})

    def test_filters_exposed_tool_definitions_based_on_context(self):
        guest_tools = self.registry.get_active_tools(role="guest")
        guest_schemas = self.registry.get_active_schemas(role="guest")
        self.assertEqual(len(guest_tools), 1)
        self.assertEqual(guest_schemas[0]["name"], "public_search")

        admin_tools = self.registry.get_active_tools(role="admin")
        admin_schemas = self.registry.get_active_schemas(role="admin")
        self.assertEqual(len(admin_tools), 2)
        names = [s["name"] for s in admin_schemas]
        self.assertIn("public_search", names)
        self.assertIn("admin_delete", names)

    def test_dispatches_invocation_to_active_tool(self):
        turn = 0
        def model_fn(state):
            nonlocal turn
            turn += 1
            if turn == 1:
                return {
                    "messages": [
                        AIMessage(
                            content="",
                            tool_calls=[{"name": "public_search", "args": {"query": "LangGraph"}, "id": "call_1"}]
                        )
                    ]
                }
            return {"messages": [AIMessage(content="Search completed")]}

        graph = build_agent_graph(model_fn=model_fn, registry=self.registry)
        result = graph.invoke({"messages": [HumanMessage(content="Search for LangGraph")]})

        tool_msgs = [m for m in result["messages"] if isinstance(m, ToolMessage)]
        self.assertEqual(len(tool_msgs), 1)
        self.assertIn("Public result for: LangGraph", tool_msgs[0].content)

    def test_rejects_invocation_of_deactivated_tool_with_informative_error(self):
        self.registry.deactivate_tool("public_search")

        turn = 0
        def model_fn(state):
            nonlocal turn
            turn += 1
            if turn == 1:
                return {
                    "messages": [
                        AIMessage(
                            content="",
                            tool_calls=[{"name": "public_search", "args": {"query": "test"}, "id": "call_2"}]
                        )
                    ]
                }
            return {"messages": [AIMessage(content="Handled deactivation")]}

        graph = build_agent_graph(model_fn=model_fn, registry=self.registry)
        result = graph.invoke({"messages": [HumanMessage(content="Run search")]})

        tool_msgs = [m for m in result["messages"] if isinstance(m, ToolMessage)]
        self.assertEqual(len(tool_msgs), 1)
        self.assertIn("Error: Tool 'public_search' is currently deactivated", tool_msgs[0].content)
        self.assertEqual(result["messages"][-1].content, "Handled deactivation")

    def test_updates_tool_schema_on_subsequent_turns(self):
        def new_calculator(expr: str) -> str:
            """Calculate an expression."""
            return "42"

        # Initially 2 tools
        initial_schemas = self.registry.get_active_schemas()
        self.assertEqual(len(initial_schemas), 2)

        # Dynamically register a new tool mid-session
        self.registry.register_tool(new_calculator)

        # Check updated schemas for subsequent turn
        updated_schemas = self.registry.get_active_schemas()
        self.assertEqual(len(updated_schemas), 3)
        self.assertIn("new_calculator", [s["name"] for s in updated_schemas])


if __name__ == "__main__":
    unittest.main()
