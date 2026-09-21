"""
OpenAI Agents SDK application with Structured Telemetry Hooks.
"""
from openai.types.responses.response_usage import InputTokensDetails
_orig_input_tokens_init = InputTokensDetails.__init__
def _safe_input_tokens_init(self, *args, **kwargs):
    if "cache_write_tokens" not in kwargs:
        kwargs["cache_write_tokens"] = 0
    _orig_input_tokens_init(self, *args, **kwargs)
InputTokensDetails.__init__ = _safe_input_tokens_init

import json
from typing import Optional, Callable
from agents import Agent, Runner, function_tool, set_tracing_disabled
from agents.models.interface import Model, ModelResponse
from openai.types.responses.response_output_message import ResponseOutputMessage, ResponseOutputText
from openai.types.responses.response_function_tool_call import ResponseFunctionToolCall
from agents.usage import Usage

try:
    from telemetry import StructuredTelemetryRunHooks
except ImportError:
    from research.runs.openai_agents_T02.telemetry import StructuredTelemetryRunHooks

set_tracing_disabled(True)


@function_tool
def search_knowledge(query: str) -> str:
    """Tool searching internal knowledge base."""
    return f"Information about {query}"


class TelemetryMockModel(Model):
    def __init__(self, with_tool: bool = False):
        self.with_tool = with_tool
        self.turn = 0

    async def get_response(self, system_instructions, input, model_settings, tools, *args, **kwargs):
        self.turn += 1
        if self.with_tool and self.turn == 1:
            tool_call = ResponseFunctionToolCall(
                call_id="call_t02",
                name="search_knowledge",
                arguments=json.dumps({"query": "distributed systems"}),
                type="function_call"
            )
            return ModelResponse(output=[tool_call], usage=Usage(input_tokens=12, output_tokens=18), response_id="r1", request_id="req1")
        msg = ResponseOutputMessage(
            id="msg_t02",
            content=[ResponseOutputText(annotations=[], text="Telemetry execution completed", type="output_text")],
            role="assistant",
            status="completed",
            type="message"
        )
        return ModelResponse(output=[msg], usage=Usage(input_tokens=25, output_tokens=30), response_id="r2", request_id="req2")

    async def stream_response(self, *args, **kwargs):
        raise NotImplementedError()


def run_agent_with_telemetry(
    prompt: str,
    with_tool: bool = False,
    caller_id: str = "custom_operator",
    hooks: Optional[StructuredTelemetryRunHooks] = None
):
    active_hooks = hooks or StructuredTelemetryRunHooks(caller_id=caller_id)
    model = TelemetryMockModel(with_tool=with_tool)
    tools = [search_knowledge] if with_tool else []
    agent = Agent(name="telemetry_agent", tools=tools, model=model)

    result = Runner.run_sync(agent, prompt, hooks=active_hooks)
    return result, active_hooks
