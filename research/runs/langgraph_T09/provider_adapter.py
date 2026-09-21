"""
Model Provider Substitution Adapter for LangGraph Model Node.
Supports adapting alternate LLM providers (e.g., Anthropic Claude format) to LangGraph.
"""
from typing import Any, Callable, Dict, Iterator, List, Optional
from langchain_core.messages import BaseMessage, SystemMessage, HumanMessage, AIMessage, ToolMessage


class AnthropicProviderAdapter:
    """
    Translates standard LangGraph message history to Anthropic schema,
    calls provider endpoint (or client mock), and translates responses/streams back.
    """
    def __init__(self, client_fn: Optional[Callable] = None):
        self.client_fn = client_fn

    def format_request(self, messages: List[BaseMessage], tools: Optional[List[Any]] = None) -> Dict[str, Any]:
        system_prompt = ""
        anthropic_msgs = []

        for m in messages:
            if isinstance(m, SystemMessage):
                system_prompt = str(m.content)
            elif isinstance(m, HumanMessage):
                anthropic_msgs.append({"role": "user", "content": m.content})
            elif isinstance(m, AIMessage):
                if getattr(m, "tool_calls", None):
                    content_blocks = []
                    if m.content:
                        content_blocks.append({"type": "text", "text": m.content})
                    for tc in m.tool_calls:
                        content_blocks.append({
                            "type": "tool_use",
                            "id": tc.get("id", "call_1"),
                            "name": tc.get("name"),
                            "input": tc.get("args", {}),
                        })
                    anthropic_msgs.append({"role": "assistant", "content": content_blocks})
                else:
                    anthropic_msgs.append({"role": "assistant", "content": m.content})
            elif isinstance(m, ToolMessage):
                anthropic_msgs.append({
                    "role": "user",
                    "content": [{
                        "type": "tool_result",
                        "tool_use_id": m.tool_call_id,
                        "content": str(m.content),
                    }]
                })

        formatted = {"messages": anthropic_msgs}
        if system_prompt:
            formatted["system"] = system_prompt

        if tools:
            formatted["tools"] = [
                {
                    "name": t.__name__ if hasattr(t, "__name__") else getattr(t, "name", str(t)),
                    "description": getattr(t, "__doc__", "") or "Tool description",
                    "input_schema": {"type": "object", "properties": {}},
                }
                for t in tools
            ]
        return formatted

    def map_finish_reason(self, provider_stop_reason: str) -> str:
        mapping = {
            "end_turn": "stop",
            "stop_sequence": "stop",
            "tool_use": "tool_calls",
            "max_tokens": "length",
        }
        return mapping.get(provider_stop_reason, provider_stop_reason)

    def parse_response(self, response_payload: Dict[str, Any]) -> AIMessage:
        stop_reason = response_payload.get("stop_reason", "end_turn")
        finish_reason = self.map_finish_reason(stop_reason)

        content_text = ""
        tool_calls = []

        content = response_payload.get("content", [])
        if isinstance(content, str):
            content_text = content
        elif isinstance(content, list):
            for block in content:
                if block.get("type") == "text":
                    content_text += block.get("text", "")
                elif block.get("type") == "tool_use":
                    tool_calls.append({
                        "name": block.get("name"),
                        "args": block.get("input", {}),
                        "id": block.get("id"),
                    })

        return AIMessage(
            content=content_text,
            tool_calls=tool_calls,
            response_metadata={"finish_reason": finish_reason, "model_provider": "anthropic"}
        )

    def translate_stream_chunk(self, chunk: Dict[str, Any]) -> Dict[str, Any]:
        c_type = chunk.get("type")
        if c_type == "content_block_delta":
            return {"type": "text_delta", "delta": chunk.get("delta", {}).get("text", "")}
        elif c_type == "content_block_start" and chunk.get("content_block", {}).get("type") == "tool_use":
            block = chunk["content_block"]
            return {"type": "tool_call_start", "name": block.get("name"), "id": block.get("id")}
        elif c_type == "message_stop":
            return {"type": "finish", "finish_reason": self.map_finish_reason(chunk.get("stop_reason", "end_turn"))}
        return {"type": "other", "raw": chunk}

    def as_model_node(self, tools: Optional[List[Any]] = None) -> Callable:
        def model_node(state: Dict[str, Any]) -> Dict[str, Any]:
            req = self.format_request(state.get("messages", []), tools=tools)
            raw_res = self.client_fn(req)
            ai_msg = self.parse_response(raw_res)
            return {"messages": [ai_msg]}
        return model_node
