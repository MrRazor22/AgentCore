import time
import tracemalloc
from pydantic_ai import Agent
from pydantic_ai.models.test import TestModel

def test_pydantic_ai():
    # 1. Setup agent with TestModel (mock zero network)
    agent = Agent(
        TestModel(custom_result_text="The weather in London is 15C and sunny."),
        system_prompt="You are a helpful assistant."
    )
    
    @agent.tool_plain
    def get_weather(city: str) -> str:
        return f"15C and sunny in {city}"
    
    tracemalloc.start()
    start = time.perf_counter_ns()
    
    # 2. Run 1 turn with tool call
    result = agent.run_sync("What is the weather in London?")
    
    duration_ns = time.perf_counter_ns() - start
    current, peak = tracemalloc.get_traced_memory()
    tracemalloc.stop()
    
    print(f"[PydanticAI] Latency: {duration_ns / 1_000:.2f} us | Peak Memory: {peak / 1024:.2f} KB | Result: {result.data}")

if __name__ == "__main__":
    test_pydantic_ai()
