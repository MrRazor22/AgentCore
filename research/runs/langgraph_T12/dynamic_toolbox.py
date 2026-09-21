"""
Dynamic Tool Discovery and Registry for LangGraph.
Allows adding, removing, and filtering active tools per turn or based on context.
"""
from typing import Any, Callable, Dict, List, Optional, Set
from langchain_core.messages import ToolMessage


class DynamicToolRegistry:
    def __init__(self):
        self._tools: Dict[str, Dict[str, Any]] = {}

    def register_tool(
        self,
        func: Callable,
        name: Optional[str] = None,
        active: bool = True,
        allowed_roles: Optional[Set[str]] = None,
    ):
        tool_name = name or func.__name__
        self._tools[tool_name] = {
            "func": func,
            "name": tool_name,
            "active": active,
            "allowed_roles": allowed_roles or set(),
            "doc": getattr(func, "__doc__", "") or "Tool description",
        }

    def activate_tool(self, name: str):
        if name in self._tools:
            self._tools[name]["active"] = True

    def deactivate_tool(self, name: str):
        if name in self._tools:
            self._tools[name]["active"] = False

    def get_active_tools(self, role: Optional[str] = None) -> List[Callable]:
        active = []
        for meta in self._tools.values():
            if not meta["active"]:
                continue
            if role and meta["allowed_roles"] and role not in meta["allowed_roles"]:
                continue
            active.append(meta["func"])
        return active

    def get_active_schemas(self, role: Optional[str] = None) -> List[Dict[str, Any]]:
        schemas = []
        for meta in self._tools.values():
            if not meta["active"]:
                continue
            if role and meta["allowed_roles"] and role not in meta["allowed_roles"]:
                continue
            schemas.append({
                "name": meta["name"],
                "description": meta["doc"],
            })
        return schemas

    def execute(self, name: str, args: Dict[str, Any], role: Optional[str] = None) -> str:
        if name not in self._tools:
            return f"Error: Tool '{name}' not found."
        meta = self._tools[name]
        if not meta["active"]:
            return f"Error: Tool '{name}' is currently deactivated."
        if role and meta["allowed_roles"] and role not in meta["allowed_roles"]:
            return f"Error: Tool '{name}' is unauthorized for role '{role}'."
        return str(meta["func"](**args))


class DynamicToolNode:
    def __init__(self, registry: DynamicToolRegistry):
        self.registry = registry

    def __call__(self, state: Dict[str, Any]) -> Dict[str, Any]:
        last_msg = state.get("messages", [])[-1]
        tool_calls = getattr(last_msg, "tool_calls", [])
        if not tool_calls:
            return {}

        results = []
        role = state.get("role")
        for tc in tool_calls:
            name = tc.get("name")
            args = tc.get("args", {})
            call_id = tc.get("id", "call_id")
            output = self.registry.execute(name, args, role=role)
            results.append(ToolMessage(content=output, tool_call_id=call_id))

        return {"messages": results}
