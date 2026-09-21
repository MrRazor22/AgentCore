"""
Context Compaction and Summarization Node for LangGraph.
"""
from typing import Any, Callable, Dict, List, Optional
from langchain_core.messages import BaseMessage, SystemMessage, HumanMessage, AIMessage, RemoveMessage


def estimate_tokens(messages: List[BaseMessage]) -> int:
    total_words = 0
    for m in messages:
        content = str(getattr(m, "content", ""))
        total_words += len(content.split())
    # Approximation: 1 word ~ 1.3 tokens
    return int(total_words * 1.3)


class ContextCompactor:
    def __init__(self, max_tokens: int = 50, preserve_recent: int = 2, summarizer_fn: Optional[Callable] = None):
        self.max_tokens = max_tokens
        self.preserve_recent = preserve_recent
        self.summarizer_fn = summarizer_fn or self._default_summarizer

    def _default_summarizer(self, messages: List[BaseMessage]) -> str:
        topics = []
        for m in messages:
            content = str(getattr(m, "content", ""))
            topics.append(content[:20].strip())
        return f"Summary of earlier conversation ({len(messages)} messages): " + "; ".join(topics)

    def compact(self, state: Dict[str, Any]) -> Dict[str, Any]:
        messages = state.get("messages", [])
        if not messages:
            return {}

        current_tokens = estimate_tokens(messages)
        if current_tokens <= self.max_tokens:
            return {}

        has_system = isinstance(messages[0], SystemMessage)
        system_msg = messages[0] if has_system else None
        rest = messages[1:] if has_system else messages

        if len(rest) <= self.preserve_recent:
            return {}

        to_summarize = rest[:-self.preserve_recent]
        recent = rest[-self.preserve_recent:]

        summary_text = self.summarizer_fn(to_summarize)
        summary_msg = SystemMessage(content=summary_text)

        # Remove to_summarize and recent, then add summary and re-add recent to maintain proper order
        removals = [RemoveMessage(id=m.id) for m in (to_summarize + recent)]
        readded_recent = [m.__class__(content=m.content) for m in recent]

        return {"messages": removals + [summary_msg] + readded_recent}
