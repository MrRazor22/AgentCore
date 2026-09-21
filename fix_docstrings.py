with open("research/runs/evolution/langgraph/agent_graph.py", "r", encoding="utf-8") as f:
    text = f.read()

text = text.replace('def default_tool(city: str) -> str:\n    return f"Weather in {city}: 72F and Sunny"',
                    'def default_tool(city: str) -> str:\n    """Get weather for city."""\n    return f"Weather in {city}: 72F and Sunny"')

with open("research/runs/evolution/langgraph/agent_graph.py", "w", encoding="utf-8") as f:
    f.write(text)

with open("research/runs/evolution/langgraph/test_langgraph_evolution.py", "r", encoding="utf-8") as f:
    t_text = f.read()

t_text = t_text.replace('def sensitive_tool(command: str) -> str:\n            if not approved:',
                        'def sensitive_tool(command: str) -> str:\n            """Execute command."""\n            if not approved:')
t_text = t_text.replace('def math_subagent_tool(query: str) -> str:\n            res = child_agent.invoke',
                        'def math_subagent_tool(query: str) -> str:\n            """Math subagent tool."""\n            res = child_agent.invoke')

with open("research/runs/evolution/langgraph/test_langgraph_evolution.py", "w", encoding="utf-8") as f:
    f.write(t_text)

print("Added docstrings to LangGraph evolution tools")
