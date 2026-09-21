"""
Real-time Stream Observation Tap for LangGraph.
"""
import time
from typing import Any, Dict, Iterator, List, Optional
from dataclasses import dataclass, field


@dataclass
class ObservedStreamEvent:
    event_type: str  # "token_delta", "tool_call_delta", "completion"
    payload: Any
    timestamp: float = field(default_factory=time.time)


class StreamObserver:
    def __init__(self):
        self.events: List[ObservedStreamEvent] = []
        self.token_deltas: List[str] = []
        self.tool_deltas: List[Dict[str, Any]] = []
        self.completed: bool = False

    def observe(self, stream_iterator: Iterator[Any]) -> Iterator[Any]:
        """
        Passes chunks transparently to downstream consumer without buffering,
        while observing token deltas, tool call deltas, and completion.
        """
        for chunk in stream_iterator:
            # Handle tuple format (stream_mode=[...]) or dict format
            data = chunk[1] if isinstance(chunk, tuple) and len(chunk) == 2 else chunk

            if isinstance(data, dict):
                c_type = data.get("type")
                if c_type == "token_chunk" or c_type == "text_delta":
                    token = data.get("content") or data.get("delta", "")
                    self.token_deltas.append(token)
                    self.events.append(ObservedStreamEvent("token_delta", token))
                elif c_type == "tool_chunk" or c_type == "tool_call_delta":
                    tc_data = data.get("tool_call", data)
                    self.tool_deltas.append(tc_data)
                    self.events.append(ObservedStreamEvent("tool_call_delta", tc_data))

            yield chunk

        self.completed = True
        self.events.append(ObservedStreamEvent("completion", {"status": "complete"}))
