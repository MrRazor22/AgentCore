"""
OpenAI Agents SDK application with Tool Approval (HITL) (Task T03).
"""
from openai.types.responses.response_usage import InputTokensDetails
_orig_input_tokens_init = InputTokensDetails.__init__
def _safe_input_tokens_init(self, *args, **kwargs):
    if "cache_write_tokens" not in kwargs:
        kwargs["cache_write_tokens"] = 0
    _orig_input_tokens_init(self, *args, **kwargs)
InputTokensDetails.__init__ = _safe_input_tokens_init

import json
from typing import Optional, Callable, Any
from agents import Agent, Runner, function_tool, set_tracing_disabled
from agents.models.interface import Model, ModelResponse
from openai.types.responses.response_output_message import ResponseOutputMessage, ResponseOutputText
from openai.types.responses.response_function_tool_call import ResponseFunctionToolCall
from agents.usage import Usage

try:
    from approval_policy import SensitiveToolApprovalPolicy
except ImportError:
    from research.runs.openai_agents_T03.approval_policy import SensitiveToolApprovalPolicy

set_tracing_disabled(True)


class ApprovalMockModel(Model):
    """Mock model that invokes requested tool on turn 1, then finishes on turn 2."""
    def __init__(self, tool_name: str = "execute_payment", tool_args: dict | None = None):
        self.tool_name = tool_name
        self.tool_args = tool_args or {"amount": 500, "recipient": "vendor_corp"}
        self.turn = 0

    async def get_response(self, system_instructions, input, model_settings, tools, *args, **kwargs):
        self.turn += 1
        if self.turn == 1:
            call = ResponseFunctionToolCall(
                call_id="call_t03_1",
                name=self.tool_name,
                arguments=json.dumps(self.tool_args),
                type="function_call"
            )
            return ModelResponse(output=[call], usage=Usage(), response_id="resp_1", request_id="req_1")

        msg = ResponseOutputMessage(
            id="msg_t03_2",
            content=[ResponseOutputText(annotations=[], text="Workflow completed successfully", type="output_text")],
            role="assistant",
            status="completed",
            type="message"
        )
        return ModelResponse(output=[msg], usage=Usage(), response_id="resp_2", request_id="req_2")

    async def stream_response(self, *args, **kwargs):
        raise NotImplementedError()


def build_tools(policy: SensitiveToolApprovalPolicy, execution_log: list):
    @function_tool(needs_approval=policy.is_sensitive("execute_payment"))
    def execute_payment(amount: int, recipient: str) -> str:
        execution_log.append(("execute_payment", amount, recipient))
        return f"Transferred ${amount} to {recipient}"

    @function_tool(needs_approval=policy.is_sensitive("query_account"))
    def query_account(account_id: str) -> str:
        execution_log.append(("query_account", account_id))
        return f"Account {account_id} balance is $10000"

    return [execute_payment, query_account]


def create_approval_agent(
    policy: Optional[SensitiveToolApprovalPolicy] = None,
    model: Optional[Model] = None,
    execution_log: Optional[list] = None
):
    active_policy = policy or SensitiveToolApprovalPolicy()
    active_log = execution_log if execution_log is not None else []
    tools = build_tools(active_policy, active_log)
    active_model = model or ApprovalMockModel()
    agent = Agent(name="approval_agent", tools=tools, model=active_model)
    return agent, active_policy, active_log
