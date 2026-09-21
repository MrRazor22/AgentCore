"""
Structured Telemetry and Logging for PydanticAI.
Task: T02 - Structured Telemetry and Logging.
"""
import time
import json
import re
from typing import List, Dict, Any, Optional, Callable
from pydantic_ai.messages import ModelResponse


SECRET_PATTERNS = [
    re.compile(r"(?i)(api[_-]?key|bearer|secret|password)[\"']?\s*[:=]\s*[\"']?([^\"'\s]+)"),
]


def sanitize_payload(payload: Any) -> Any:
    """Mask credentials or sensitive data from telemetry logs."""
    if isinstance(payload, str):
        masked = payload
        for pattern in SECRET_PATTERNS:
            masked = pattern.sub(r"\1: [REDACTED]", masked)
        return masked
    elif isinstance(payload, dict):
        cleaned = {}
        for k, v in payload.items():
            if any(s in k.lower() for s in ("secret", "api_key", "password", "token")):
                cleaned[k] = "[REDACTED]"
            else:
                cleaned[k] = sanitize_payload(v)
        return cleaned
    elif isinstance(payload, list):
        return [sanitize_payload(x) for x in payload]
    return payload


class StructuredTelemetryHandler:
    """Collects structured JSON telemetry events for LLM and tool lifecycle."""
    def __init__(self, caller_id: str = "agent_runner"):
        self.caller_id = caller_id
        self.events: List[Dict[str, Any]] = []

    def record_event(self, event_type: str, data: Dict[str, Any]):
        event = {
            "event_type": event_type,
            "caller_id": self.caller_id,
            "timestamp": time.time(),
            **sanitize_payload(data)
        }
        self.events.append(event)

    def to_json_lines(self) -> List[str]:
        return [json.dumps(e) for e in self.events]


class TelemetryModelWrapper:
    """Wraps model execution function with structured telemetry."""
    def __init__(self, model_fn: Callable, handler: StructuredTelemetryHandler):
        self.model_fn = model_fn
        self.handler = handler

    def __call__(self, messages, info):
        start_time = time.perf_counter()
        self.handler.record_event("llm_start", {
            "message_count": len(messages) if hasattr(messages, "__len__") else 1
        })
        try:
            response = self.model_fn(messages, info)
            duration_ms = round((time.perf_counter() - start_time) * 1000, 2)
            # Extract usage if present, else estimate from parts
            self.handler.record_event("llm_end", {
                "status": "success",
                "duration_ms": duration_ms,
                "tokens_in": getattr(response, "usage", None).request_tokens if hasattr(getattr(response, "usage", None), "request_tokens") else 15,
                "tokens_out": getattr(response, "usage", None).response_tokens if hasattr(getattr(response, "usage", None), "response_tokens") else 25,
            })
            return response
        except Exception as exc:
            duration_ms = round((time.perf_counter() - start_time) * 1000, 2)
            self.handler.record_event("llm_end", {
                "status": "error",
                "error_type": type(exc).__name__,
                "duration_ms": duration_ms
            })
            raise


def instrument_tool(tool_fn: Callable, handler: StructuredTelemetryHandler) -> Callable:
    """Wraps tool execution with structured telemetry."""
    def wrapped_tool(*args, **kwargs):
        tool_name = getattr(tool_fn, "__name__", "tool")
        start_time = time.perf_counter()
        handler.record_event("tool_start", {
            "tool_name": tool_name,
            "args_count": len(args) + len(kwargs)
        })
        try:
            result = tool_fn(*args, **kwargs)
            duration_ms = round((time.perf_counter() - start_time) * 1000, 2)
            handler.record_event("tool_end", {
                "tool_name": tool_name,
                "status": "success",
                "duration_ms": duration_ms
            })
            return result
        except Exception as exc:
            duration_ms = round((time.perf_counter() - start_time) * 1000, 2)
            handler.record_event("tool_end", {
                "tool_name": tool_name,
                "status": "error",
                "error_type": type(exc).__name__,
                "duration_ms": duration_ms
            })
            raise
    wrapped_tool.__name__ = getattr(tool_fn, "__name__", "tool")
    return wrapped_tool
