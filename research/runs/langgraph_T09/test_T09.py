"""
Acceptance tests for Task T09: Model Provider Substitution.
"""
import unittest
from langchain_core.messages import SystemMessage, HumanMessage, AIMessage, ToolMessage

try:
    from agent_graph import build_agent_graph
    from provider_adapter import AnthropicProviderAdapter
except ImportError:
    from research.runs.langgraph_T09.agent_graph import build_agent_graph
    from research.runs.langgraph_T09.provider_adapter import AnthropicProviderAdapter


def lookup_stock(symbol: str) -> str:
    """Lookup current price of stock symbol."""
    return f"Stock {symbol}: $150.00"


class TestLangGraphT09ProviderSubstitution(unittest.TestCase):
    def test_maps_messages_and_tools_to_provider_schema(self):
        adapter = AnthropicProviderAdapter()
        messages = [
            SystemMessage(content="You are a financial advisor."),
            HumanMessage(content="What is AAPL price?"),
            AIMessage(
                content="Checking stock price...",
                tool_calls=[{"name": "lookup_stock", "args": {"symbol": "AAPL"}, "id": "call_123"}]
            ),
            ToolMessage(content="Stock AAPL: $150.00", tool_call_id="call_123"),
        ]

        req = adapter.format_request(messages, tools=[lookup_stock])

        # Check system prompt mapping
        self.assertEqual(req["system"], "You are a financial advisor.")

        # Check message roles and tool calls
        msgs = req["messages"]
        self.assertEqual(msgs[0]["role"], "user")
        self.assertEqual(msgs[0]["content"], "What is AAPL price?")

        # Check assistant tool_use content block
        self.assertEqual(msgs[1]["role"], "assistant")
        self.assertEqual(msgs[1]["content"][1]["type"], "tool_use")
        self.assertEqual(msgs[1]["content"][1]["name"], "lookup_stock")

        # Check tool_result mapping
        self.assertEqual(msgs[2]["role"], "user")
        self.assertEqual(msgs[2]["content"][0]["type"], "tool_result")
        self.assertEqual(msgs[2]["content"][0]["tool_use_id"], "call_123")

        # Check tool definitions schema
        self.assertEqual(len(req["tools"]), 1)
        self.assertEqual(req["tools"][0]["name"], "lookup_stock")

    def test_translates_provider_streaming_chunks(self):
        adapter = AnthropicProviderAdapter()

        chunk_text = {"type": "content_block_delta", "delta": {"type": "text_delta", "text": "Hello world"}}
        t_text = adapter.translate_stream_chunk(chunk_text)
        self.assertEqual(t_text["type"], "text_delta")
        self.assertEqual(t_text["delta"], "Hello world")

        chunk_tool = {"type": "content_block_start", "content_block": {"type": "tool_use", "id": "t_1", "name": "lookup_stock"}}
        t_tool = adapter.translate_stream_chunk(chunk_tool)
        self.assertEqual(t_tool["type"], "tool_call_start")
        self.assertEqual(t_tool["name"], "lookup_stock")

        chunk_stop = {"type": "message_stop", "stop_reason": "tool_use"}
        t_stop = adapter.translate_stream_chunk(chunk_stop)
        self.assertEqual(t_stop["type"], "finish")
        self.assertEqual(t_stop["finish_reason"], "tool_calls")

    def test_handles_provider_specific_finish_reason_mapping(self):
        adapter = AnthropicProviderAdapter()
        self.assertEqual(adapter.map_finish_reason("end_turn"), "stop")
        self.assertEqual(adapter.map_finish_reason("tool_use"), "tool_calls")
        self.assertEqual(adapter.map_finish_reason("max_tokens"), "length")

    def test_executes_agent_loop_with_alternate_provider(self):
        # Mock client function emulating Anthropic API
        turn = 0
        def anthropic_mock_api(request: dict) -> dict:
            nonlocal turn
            turn += 1
            if turn == 1:
                return {
                    "stop_reason": "tool_use",
                    "content": [
                        {"type": "text", "text": "Let me look up the stock."},
                        {"type": "tool_use", "id": "call_stock_1", "name": "lookup_stock", "input": {"symbol": "MSFT"}}
                    ]
                }
            else:
                return {
                    "stop_reason": "end_turn",
                    "content": [
                        {"type": "text", "text": "MSFT is trading at $150.00."}
                    ]
                }

        adapter = AnthropicProviderAdapter(client_fn=anthropic_mock_api)
        graph = build_agent_graph(provider_adapter=adapter, tools=[lookup_stock])

        result = graph.invoke({"messages": [HumanMessage(content="Price of MSFT?")]})
        messages = result["messages"]

        self.assertGreaterEqual(len(messages), 4) # human, ai(tool_call), tool_result, ai(final)
        final_msg = messages[-1]
        self.assertEqual(final_msg.content, "MSFT is trading at $150.00.")
        self.assertEqual(final_msg.response_metadata["finish_reason"], "stop")
        self.assertEqual(final_msg.response_metadata["model_provider"], "anthropic")


if __name__ == "__main__":
    unittest.main()
