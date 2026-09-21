"""
Acceptance tests for Task T08: Context Compaction and Summarization Node.
"""
import unittest
from langchain_core.messages import SystemMessage, HumanMessage, AIMessage

try:
    from agent_graph import build_agent_graph
    from context_compactor import ContextCompactor, estimate_tokens
except ImportError:
    from research.runs.langgraph_T08.agent_graph import build_agent_graph
    from research.runs.langgraph_T08.context_compactor import ContextCompactor, estimate_tokens


class TestLangGraphT08Compaction(unittest.TestCase):
    def test_monitors_token_length_and_bypasses_when_below_threshold(self):
        compactor = ContextCompactor(max_tokens=1000, preserve_recent=2)
        graph = build_agent_graph(compactor=compactor)

        initial_messages = [
            SystemMessage(content="You are a helpful assistant."),
            HumanMessage(content="Hello!"),
            AIMessage(content="Hi there!"),
        ]
        result = graph.invoke({"messages": initial_messages})
        messages = result["messages"]

        # Below threshold: no messages replaced by summary
        self.assertEqual(len(messages), 4) # initial 3 + model response
        self.assertEqual(messages[0].content, "You are a helpful assistant.")
        self.assertEqual(messages[1].content, "Hello!")

    def test_replaces_oldest_turns_with_summary_when_exceeding_threshold(self):
        # Set low token threshold to force compaction
        compactor = ContextCompactor(max_tokens=20, preserve_recent=2)
        graph = build_agent_graph(compactor=compactor)

        initial_messages = [
            SystemMessage(content="You are a specialized code analyzer."),
            HumanMessage(content="Turn 1: Please analyze this very long legacy codebase with multiple modules."),
            AIMessage(content="Turn 1 reply: I will examine the architecture components and dependencies."),
            HumanMessage(content="Turn 2: Also analyze the database migration logs and queries in detail."),
            AIMessage(content="Turn 2 reply: Found 50 database migration steps and potential query locks."),
            HumanMessage(content="Turn 3: What is the recommended fix?"),
        ]

        result = graph.invoke({"messages": initial_messages})
        messages = result["messages"]

        # Check that a summary message was introduced
        summary_msgs = [m for m in messages if "Summary of earlier conversation" in str(m.content)]
        self.assertEqual(len(summary_msgs), 1)

    def test_preserves_system_prompt_and_last_n_turns_verbatim(self):
        compactor = ContextCompactor(max_tokens=20, preserve_recent=2)
        graph = build_agent_graph(compactor=compactor)

        system_prompt = "Permanent core system prompt: do not summarize."
        recent_1 = "Turn 2: Highly specific recent question."
        recent_2 = "Turn 2 response: Highly specific recent answer."

        initial_messages = [
            SystemMessage(content=system_prompt),
            HumanMessage(content="Old message 1 with extensive explanations about history."),
            AIMessage(content="Old message 2 with extensive technical details and logs."),
            HumanMessage(content=recent_1),
            AIMessage(content=recent_2),
        ]

        result = graph.invoke({"messages": initial_messages})
        messages = result["messages"]

        # System prompt at index 0 must be preserved verbatim
        self.assertEqual(messages[0].content, system_prompt)
        # Recent messages must be preserved verbatim
        contents = [m.content for m in messages]
        self.assertIn(recent_1, contents)
        self.assertIn(recent_2, contents)

    def test_maintains_correct_message_ordering_and_turn_semantics(self):
        compactor = ContextCompactor(max_tokens=15, preserve_recent=2)
        graph = build_agent_graph(compactor=compactor)

        system_msg = SystemMessage(content="System instruction.")
        h1 = HumanMessage(content="Old question 1")
        a1 = AIMessage(content="Old answer 1")
        h2 = HumanMessage(content="Recent question 2")
        a2 = AIMessage(content="Recent answer 2")

        result = graph.invoke({"messages": [system_msg, h1, a1, h2, a2]})
        messages = result["messages"]

        # Expected order: [SystemMessage, Summary(SystemMessage), Recent(HumanMessage), Recent(AIMessage), New AIMessage]
        self.assertEqual(messages[0].content, "System instruction.")
        self.assertIn("Summary", messages[1].content)
        self.assertEqual(messages[2].content, "Recent question 2")
        self.assertEqual(messages[3].content, "Recent answer 2")
        self.assertEqual(messages[4].content, "Response from baseline model.")


if __name__ == "__main__":
    unittest.main()
