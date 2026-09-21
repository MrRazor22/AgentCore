"""
Response and Semantic Caching for LangGraph Model Node.
"""
import hashlib
import json
import time
from typing import Any, Callable, Dict, List, Optional
from langchain_core.messages import AIMessage, BaseMessage


class ResponseCache:
    def __init__(self, ttl_seconds: float = 60.0):
        self.ttl_seconds = ttl_seconds
        self._cache: Dict[str, Dict[str, Any]] = {}

    def derive_key(self, messages: List[BaseMessage], system_prompt: Optional[str] = None, tools_info: Optional[str] = None) -> str:
        tokens = []
        if system_prompt:
            tokens.append(f"system:{system_prompt}")
        if tools_info:
            tokens.append(f"tools:{tools_info}")
        for m in messages:
            role = getattr(m, "type", m.__class__.__name__)
            tokens.append(f"{role}:{m.content}")
        raw = "||".join(tokens)
        return hashlib.sha256(raw.encode("utf-8")).hexdigest()

    def get(self, key: str) -> Optional[AIMessage]:
        if key not in self._cache:
            return None
        entry = self._cache[key]
        if time.time() - entry["timestamp"] > self.ttl_seconds:
            del self._cache[key]
            return None
        return entry["response"]

    def set(self, key: str, response: AIMessage):
        self._cache[key] = {
            "response": response,
            "timestamp": time.time(),
        }

    def clear(self):
        self._cache.clear()


def create_cached_model_node(model_fn: Callable, cache: ResponseCache, system_prompt: Optional[str] = None) -> Callable:
    def cached_model_fn(state: Dict[str, Any]) -> Dict[str, Any]:
        messages = state.get("messages", [])
        key = cache.derive_key(messages, system_prompt=system_prompt)
        cached_resp = cache.get(key)
        if cached_resp is not None:
            return {"messages": [cached_resp]}
        result = model_fn(state)
        # Store output in cache
        resp_msg = result.get("messages", [])[-1]
        cache.set(key, resp_msg)
        return result
    return cached_model_fn
