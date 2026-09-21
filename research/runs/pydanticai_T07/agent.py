"""
PydanticAI agent with WAL persistence layer.
"""
import os
os.environ["PYDANTIC_AI_NO_BANNER"] = "1"
from typing import Optional, Callable
from pydantic_ai import Agent
from pydantic_ai.models.function import FunctionModel
from pydantic_ai.messages import ModelResponse, TextPart

try:
    from persistence import FileWalStore, PydanticAISessionManager
except ImportError:
    from research.runs.pydanticai_T07.persistence import (
        FileWalStore,
        PydanticAISessionManager
    )


def default_model_fn(messages, info):
    return ModelResponse(parts=[TextPart("Persistent model response")])


def create_agent(
    model_fn: Optional[Callable] = None,
    wal_dir: str = "./wal_data",
    session_id: str = "sess_001"
) -> PydanticAISessionManager:
    fn = model_fn or default_model_fn
    model = FunctionModel(fn)
    agent = Agent(model)

    store = FileWalStore(wal_dir, session_id=session_id)
    session = PydanticAISessionManager(agent, store)
    return session
