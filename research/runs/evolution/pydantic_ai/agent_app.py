"""
PydanticAI Evolution Agent App
"""
import re
from typing import Callable, Optional, Any
from pydantic_ai import Agent, RunContext
from pydantic_ai.models.test import TestModel

def create_pydantic_evolution_agent(
    model: Optional[Any] = None,
    system_prompt: str = "You are a helpful assistant.",
):
    active_model = model or TestModel()
    agent = Agent(active_model, system_prompt=system_prompt)

    @agent.tool
    def get_weather(ctx: RunContext, city: str) -> str:
        return f"Weather in {city}: 72F and Sunny"

    return agent
