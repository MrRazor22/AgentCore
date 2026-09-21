"""
OpenAI Agents SDK application with Input and Tool Guardrails (Task T06).
"""
from openai.types.responses.response_usage import InputTokensDetails
_orig_input_tokens_init = InputTokensDetails.__init__
def _safe_input_tokens_init(self, *args, **kwargs):
    if "cache_write_tokens" not in kwargs:
        kwargs["cache_write_tokens"] = 0
    _orig_input_tokens_init(self, *args, **kwargs)
InputTokensDetails.__init__ = _safe_input_tokens_init

import json
from typing import Optional, List
from agents import Agent, Runner, function_tool, set_tracing_disabled, InputGuardrailTripwireTriggered
from agents.models.interface import Model, ModelResponse
from openai.types.responses.response_output_message import ResponseOutputMessage, ResponseOutputText
from openai.types.responses.response_function_tool_call import ResponseFunctionToolCall
from agents.usage import Usage

try:
    from guardrail_policy import (
        prompt_safety_guardrail,
        tool_arguments_guardrail,
        ValidationException,
    )
except ImportError:
    from research.runs.openai_agents_T06.guardrail_policy import (
        prompt_safety_guardrail,
        tool_arguments_guardrail,
        ValidationException,
    )

set_tracing_disabled(True)


class GuardrailMockModel(Model):
    def __init__(self, with_tool: bool = False, tool_args: dict | None = None):
        self.with_tool = with_tool
        self.tool_args = tool_args or {"amount": 500, "recipient": "service_acct"}
        self.turn = 0

    async def get_response(self, system_instructions, input, model_settings, tools, *args, **kwargs):
        self.turn += 1
        if self.with_tool and self.turn == 1:
            call = ResponseFunctionToolCall(
                call_id="call_t06_1",
                name="transfer_funds",
                arguments=json.dumps(self.tool_args),
                type="function_call"
            )
            return ModelResponse(output=[call], usage=Usage(), response_id="resp_g1", request_id="req_g1")

        msg = ResponseOutputMessage(
            id="msg_t06_2",
            content=[ResponseOutputText(annotations=[], text="Operation completed successfully", type="output_text")],
            role="assistant",
            status="completed",
            type="message"
        )
        return ModelResponse(output=[msg], usage=Usage(), response_id="resp_g2", request_id="req_g2")

    async def stream_response(self, *args, **kwargs):
        raise NotImplementedError()


def build_guardrail_tools(log: list):
    @function_tool(tool_input_guardrails=[tool_arguments_guardrail])
    def transfer_funds(amount: int, recipient: str) -> str:
        log.append(("transfer_funds", amount, recipient))
        return f"Transferred {amount} to {recipient}"

    return [transfer_funds]


def run_agent_guarded(
    prompt: str,
    with_tool: bool = False,
    tool_args: dict | None = None,
    execution_log: Optional[list] = None
):
    log = execution_log if execution_log is not None else []
    tools = build_guardrail_tools(log)
    model = GuardrailMockModel(with_tool=with_tool, tool_args=tool_args)

    agent = Agent(
        name="guardrail_agent",
        input_guardrails=[prompt_safety_guardrail],
        tools=tools,
        model=model
    )

    try:
        result = Runner.run_sync(agent, prompt)
        return result, log
    except InputGuardrailTripwireTriggered as exc:
        # Translate to canonical ValidationException with diagnostics for consumer
        diagnostics = {}
        if hasattr(exc, "guardrail_result") and hasattr(exc.guardrail_result, "output"):
            diagnostics = getattr(exc.guardrail_result.output, "output_info", {})
        rule = diagnostics.get("rule", "prohibited_content")
        raise ValidationException(f"Input rejected by guardrail: prohibited content detected ({rule})", diagnostics=diagnostics) from exc

