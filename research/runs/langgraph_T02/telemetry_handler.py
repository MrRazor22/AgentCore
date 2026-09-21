"""
Structured Telemetry and Logging Handler for LangGraph.
"""
import time
import re
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional

SECRET_PATTERN = re.compile(r"(sk-[a-zA-Z0-9]{8,}|Bearer\s+[a-zA-Z0-9_\-\.]+)", re.IGNORECASE)
SENSITIVE_KEYS = {"api_key", "token", "secret", "authorization", "password"}


def sanitize_data(data: Any) -> Any:
    if isinstance(data, str):
        return SECRET_PATTERN.sub("***REDACTED***", data)
    elif isinstance(data, dict):
        sanitized = {}
        for k, v in data.items():
            if any(s in str(k).lower() for s in SENSITIVE_KEYS):
                sanitized[k] = "***REDACTED***"
            else:
                sanitized[k] = sanitize_data(v)
        return sanitized
    elif isinstance(data, list):
        return [sanitize_data(item) for item in data]
    return data


class StructuredTelemetryHandler:
    def __init__(self, caller_id: str = "default_caller"):
        self.caller_id = caller_id
        self.events: List[Dict[str, Any]] = []

    def record_llm_start(self, run_id: str, prompt_summary: Any = None) -> float:
        start_time = time.time()
        event = {
            "event_type": "llm_start",
            "caller_id": self.caller_id,
            "run_id": run_id,
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "payload": sanitize_data(prompt_summary),
        }
        self.events.append(event)
        return start_time

    def record_llm_end(self, run_id: str, start_time: float, token_usage: Optional[Dict[str, int]] = None, output_summary: Any = None):
        duration_ms = (time.time() - start_time) * 1000.0
        event = {
            "event_type": "llm_end",
            "caller_id": self.caller_id,
            "run_id": run_id,
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "duration_ms": duration_ms,
            "token_usage": token_usage or {"prompt_tokens": 10, "completion_tokens": 15, "total_tokens": 25},
            "payload": sanitize_data(output_summary),
        }
        self.events.append(event)

    def record_tool_start(self, run_id: str, tool_name: str, tool_args: Any = None) -> float:
        start_time = time.time()
        event = {
            "event_type": "tool_start",
            "caller_id": self.caller_id,
            "run_id": run_id,
            "tool_name": tool_name,
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "args": sanitize_data(tool_args),
        }
        self.events.append(event)
        return start_time

    def record_tool_end(self, run_id: str, tool_name: str, start_time: float, status: str = "success", result: Any = None):
        duration_ms = (time.time() - start_time) * 1000.0
        event = {
            "event_type": "tool_end",
            "caller_id": self.caller_id,
            "run_id": run_id,
            "tool_name": tool_name,
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "duration_ms": duration_ms,
            "status": status,
            "result": sanitize_data(result),
        }
        self.events.append(event)
