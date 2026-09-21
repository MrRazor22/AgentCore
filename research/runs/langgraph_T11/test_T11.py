"""
Acceptance tests for Task T11: Controlled Interruption and Resumption.
"""
import unittest
from langchain_core.messages import HumanMessage, AIMessage
from langgraph.types import Command
from langgraph.checkpoint.memory import MemorySaver

try:
    from agent_graph import build_agent_graph
    from run_controller import RunController
except ImportError:
    from research.runs.langgraph_T11.agent_graph import build_agent_graph
    from research.runs.langgraph_T11.run_controller import RunController


class TestLangGraphT11InterruptionResumption(unittest.TestCase):
    def test_signals_interruption_without_state_corruption(self):
        controller = RunController()
        graph = build_agent_graph(controller=controller)

        config = {"configurable": {"thread_id": "thread_interrupt_signal"}}
        result = graph.invoke({"messages": [HumanMessage(content="Initial input")]}, config=config)

        state = graph.get_state(config)
        # Verify paused at review node
        self.assertEqual(state.next, ("review",))
        # Verify messages in state are completely intact without corruption
        messages = state.values.get("messages", [])
        self.assertEqual(len(messages), 2)
        self.assertEqual(messages[0].content, "Initial input")
        self.assertEqual(messages[1].content, "Response from baseline model.")

    def test_serializes_snapshot_at_point_of_interruption(self):
        checkpointer = MemorySaver()
        controller = RunController()
        graph = build_agent_graph(controller=controller, checkpointer=checkpointer)

        config = {"configurable": {"thread_id": "thread_snapshot_check"}}
        graph.invoke({"messages": [HumanMessage(content="Task step A")]}, config=config)

        # Retrieve checkpoint tuple from saver
        checkpoint_tuple = checkpointer.get_tuple(config)
        self.assertIsNotNone(checkpoint_tuple)
        self.assertIsNotNone(checkpoint_tuple.checkpoint)

        state = graph.get_state(config)
        self.assertGreater(len(state.tasks), 0)
        self.assertGreater(len(state.tasks[0].interrupts), 0)
        interrupt_val = state.tasks[0].interrupts[0].value
        self.assertEqual(interrupt_val["interruption_type"], "step_pause")

    def test_resumes_from_snapshot_upon_external_trigger(self):
        controller = RunController()
        graph = build_agent_graph(controller=controller)

        config = {"configurable": {"thread_id": "thread_resume_trigger"}}
        graph.invoke({"messages": [HumanMessage(content="First command")]}, config=config)

        state_before = graph.get_state(config)
        self.assertIn("review", state_before.next)

        # Resume with external input
        resume_payload = {"feedback": "User external confirmation"}
        result = graph.invoke(Command(resume=resume_payload), config=config)

        # State should now have transitioned past review
        state_after = graph.get_state(config)
        self.assertEqual(len(state_after.next), 0)

    def test_completes_remainder_of_task_correctly_upon_resumption(self):
        model_calls = 0
        def multi_step_model(state):
            nonlocal model_calls
            model_calls += 1
            return {"messages": [AIMessage(content=f"Model step {model_calls}")]}

        controller = RunController()
        graph = build_agent_graph(model_fn=multi_step_model, controller=controller)

        config = {"configurable": {"thread_id": "thread_full_completion"}}
        graph.invoke({"messages": [HumanMessage(content="Start process")]}, config=config)

        # Check paused at step 1
        self.assertEqual(model_calls, 1)

        # Resume
        final_state = graph.invoke(Command(resume={"feedback": "approved"}), config=config)
        messages = final_state["messages"]

        # Final state includes user input, step 1 response, and resume feedback
        self.assertEqual(messages[0].content, "Start process")
        self.assertEqual(messages[1].content, "Model step 1")
        self.assertEqual(messages[2].content, "approved")


if __name__ == "__main__":
    unittest.main()
