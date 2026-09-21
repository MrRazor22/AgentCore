"""
OpenAI Agents SDK application with Durable WAL Session Persistence (Task T07).
"""
from openai.types.responses.response_usage import InputTokensDetails
_orig_input_tokens_init = InputTokensDetails.__init__
def _safe_input_tokens_init(self, *args, **kwargs):
    if "cache_write_tokens" not in kwargs:
        kwargs["cache_write_tokens"] = 0
    _orig_input_tokens_init(self, *args, **kwargs)
InputTokensDetails.__init__ = _safe_input_tokens_init

from typing import Optional
from agents import Agent, Runner, set_tracing_disabled
from agents.models.interface import Model, ModelResponse
from openai.types.responses.response_output_message import ResponseOutputMessage, ResponseOutputText
from agents.usage import Usage

try:
    from wal_session import DurableWALSession
except ImportError:
    from research.runs.openai_agents_T07.wal_session import DurableWALSession

set_tracing_disabled(True)


class DurableMockModel(Model):
    def __init__(self, response_prefix: str = "Echo: "):
        self.response_prefix = response_prefix
        self.turns = 0

    async def get_response(self, system_instructions, input, model_settings, tools, *args, **kwargs):
        self.turns += 1
        last_text = "default"
        if input:
            for item in reversed(input):
                content = item.get("content") if isinstance(item, dict) else getattr(item, "content", None)
                if isinstance(content, str):
                    last_text = content
                    break
                elif isinstance(content, list):
                    for sub in content:
                        text_val = sub.get("text") if isinstance(sub, dict) else getattr(sub, "text", None)
                        if text_val:
                            last_text = text_val
                            break
                    if last_text != "default":
                        break

        msg = ResponseOutputMessage(
            id=f"msg_wal_{self.turns}",
            content=[ResponseOutputText(annotations=[], text=f"{self.response_prefix}{last_text}", type="output_text")],
            role="assistant",
            status="completed",
            type="message"
        )
        return ModelResponse(output=[msg], usage=Usage(), response_id=f"resp_wal_{self.turns}", request_id=f"req_wal_{self.turns}")

    async def stream_response(self, *args, **kwargs):
        raise NotImplementedError()


def create_durable_agent(model: Optional[Model] = None) -> Agent:
    raw_model = model or DurableMockModel()
    return Agent(name="durable_agent", model=raw_model)


def run_agent_in_session(
    agent: Agent,
    prompt: str,
    session: DurableWALSession
):
    result = Runner.run_sync(agent, prompt, session=session)
    # Checkpoint on successful completion
    session.checkpoint()
    return result
