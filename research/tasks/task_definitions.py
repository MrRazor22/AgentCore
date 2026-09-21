"""
Task definitions and acceptance criteria for the 12 primary benchmark tasks (T01-T12).
Frozen as part of the Primitive-First Architecture Empirical Evaluation Protocol.
"""

from dataclasses import dataclass, field
from typing import List, Dict, Set

@dataclass(frozen=True)
class TaskSpec:
    task_id: str
    name: str
    category: str  # "cross-cutting" or "state-execution"
    description: str
    natural_boundary: Set[str]  # Allowed artifact roles within B(r)
    forbidden_boundary: Set[str]  # Roles outside B(r); touching these incurs P_ext
    acceptance_criteria: List[str]
    expressiveness_seam: str  # Intended framework extension seam

TASK_CORPUS: Dict[str, TaskSpec] = {
    "T01": TaskSpec(
        task_id="T01",
        name="Retry with Exponential Backoff",
        category="cross-cutting",
        description="Catch transient provider errors (HTTP 429/503) and retry up to 3 times with exponential backoff and jitter.",
        natural_boundary={"llm_layer", "retry_policy", "retry_test", "retry_config"},
        forbidden_boundary={"core_runner", "tool_registry", "context_memory", "state_schema"},
        acceptance_criteria=[
            "Retries on HTTP 429 transient failure up to configured max attempts (3)",
            "Propagates unhandled fatal exceptions immediately without retry",
            "Succeeds transparently if retry succeeds before max attempts",
            "Calculates exponential backoff delay between attempts"
        ],
        expressiveness_seam="LLM decorator / middleware / client wrapper"
    ),
    "T02": TaskSpec(
        task_id="T02",
        name="Structured Telemetry and Logging",
        category="cross-cutting",
        description="Emit structured JSON events with timestamps, duration, tokens, and caller ID for LLM and tool operations.",
        natural_boundary={"telemetry_layer", "telemetry_handler", "telemetry_test", "telemetry_config"},
        forbidden_boundary={"core_runner", "tool_logic", "context_memory"},
        acceptance_criteria=[
            "Emits event before and after LLM generation with duration and token usage",
            "Emits event before and after tool execution with execution status and duration",
            "Passes through execution payloads without mutating model or tool events",
            "Does not leak credentials or unmasked secrets in structured logs"
        ],
        expressiveness_seam="LLM & Tool decorators / telemetry callbacks / handler pipeline"
    ),
    "T03": TaskSpec(
        task_id="T03",
        name="Tool Execution Approval (HITL)",
        category="cross-cutting",
        description="Intercept invocations of sensitive tools, suspend execution for external approval, and proceed only if granted.",
        natural_boundary={"toolbox_layer", "approval_delegate", "approval_test", "approval_config"},
        forbidden_boundary={"core_runner", "llm_client", "context_storage"},
        acceptance_criteria=[
            "Intercepts tools tagged as sensitive prior to tool invocation",
            "Executes tool normally when approval delegate returns true",
            "Aborts tool execution cleanly and returns rejection message when approval delegate returns false",
            "Does not block or prompt for non-sensitive tools"
        ],
        expressiveness_seam="Toolbox decorator / tool middleware / pre-tool callback"
    ),
    "T04": TaskSpec(
        task_id="T04",
        name="Response and Semantic Caching",
        category="cross-cutting",
        description="Cache model responses based on prompt and tool definition hash to bypass redundant upstream invocations.",
        natural_boundary={"caching_layer", "cache_store", "caching_test", "cache_config"},
        forbidden_boundary={"core_runner", "tool_registry", "context_memory"},
        acceptance_criteria=[
            "Returns cached response without invoking underlying model on identical prompt cache key",
            "Invokes model and populates cache on cache miss",
            "Includes message history and system prompt in cache key derivation",
            "Invalidates or bypasses cache when configured TTL expires"
        ],
        expressiveness_seam="LLM decorator / cache client layer"
    ),
    "T05": TaskSpec(
        task_id="T05",
        name="Rate Limiting (Token Bucket)",
        category="cross-cutting",
        description="Enforce client-side rate limits on outgoing model requests using a token bucket algorithm.",
        natural_boundary={"rate_limit_layer", "token_bucket", "rate_limit_test", "rate_limit_config"},
        forbidden_boundary={"core_runner", "tool_registry", "context_memory"},
        acceptance_criteria=[
            "Enforces maximum call rate per second and burst capacity",
            "Delays or throttles request until token capacity replenishes",
            "Throws RateLimitExceededException when non-blocking queue capacity exceeded",
            "Thread-safe token consumption under concurrent requests"
        ],
        expressiveness_seam="LLM decorator / client rate limiter"
    ),
    "T06": TaskSpec(
        task_id="T06",
        name="Input Validation and Guardrails",
        category="cross-cutting",
        description="Validate input prompts and tool arguments against safety rules, rejecting invalid inputs prior to dispatch.",
        natural_boundary={"guardrail_layer", "validation_policy", "guardrail_test", "guardrail_config"},
        forbidden_boundary={"core_runner", "tool_registry", "context_memory"},
        acceptance_criteria=[
            "Rejects prompt matching prohibited patterns with ValidationException before LLM call",
            "Validates tool arguments against constraints prior to tool invocation",
            "Allows benign inputs to proceed unmodified",
            "Emits structured validation failure diagnostics"
        ],
        expressiveness_seam="LLM / Tool input decorator / validator filter"
    ),
    "T07": TaskSpec(
        task_id="T07",
        name="Durable Context and Session Recovery",
        category="state-execution",
        description="Persist conversation events using Write-Ahead Logging (WAL) to survive abrupt process termination and resume seamlessly.",
        natural_boundary={"context_layer", "wal_store", "persistence_test", "persistence_config"},
        forbidden_boundary={"core_runner", "llm_client", "tool_registry"},
        acceptance_criteria=[
            "Appends incoming and generated message events to durable store synchronously",
            "Recovers full conversational history upon rehydration from disk/store",
            "Clears temporary WAL uncommitted chunks upon successful checkpoint",
            "Recovers gracefully without history truncation after simulated crash"
        ],
        expressiveness_seam="Context decorator / checkpointer / session store"
    ),
    "T08": TaskSpec(
        task_id="T08",
        name="Context Compaction and Summarization",
        category="state-execution",
        description="Compact conversation history when token count crosses threshold by summarizing older turns while keeping recent turns intact.",
        natural_boundary={"context_compactor", "summarizer", "compactor_test", "compactor_config"},
        forbidden_boundary={"core_runner", "llm_client", "tool_registry"},
        acceptance_criteria=[
            "Monitors conversation token length against configured threshold",
            "Replaces oldest turns with concise summary message when threshold exceeded",
            "Preserves system prompt and last N turns verbatim",
            "Maintains correct message ordering and turn semantics after compaction"
        ],
        expressiveness_seam="Context decorator / context modifier / memory trimmer"
    ),
    "T09": TaskSpec(
        task_id="T09",
        name="Model Provider Substitution",
        category="state-execution",
        description="Switch underlying LLM provider (e.g., from OpenAI to Anthropic/DeepSeek) without changing agent execution logic.",
        natural_boundary={"llm_provider_adapter", "provider_test", "provider_config"},
        forbidden_boundary={"core_runner", "tool_registry", "context_memory"},
        acceptance_criteria=[
            "Maps standard message history and tool schemas to new provider format",
            "Translates provider streaming chunks to standard event stream",
            "Executes complete agent loop with zero change to tool or context implementations",
            "Handles provider-specific finish reason mapping"
        ],
        expressiveness_seam="ILLM implementation / ModelClient adapter"
    ),
    "T10": TaskSpec(
        task_id="T10",
        name="Real-time Stream Observation",
        category="state-execution",
        description="Observe fine-grained streaming chunks (token deltas, tool call deltas) as they are generated without breaking iteration.",
        natural_boundary={"stream_observer", "stream_tap", "stream_test", "stream_config"},
        forbidden_boundary={"core_runner", "tool_registry", "context_memory"},
        acceptance_criteria=[
            "Yields token deltas in real time as emitted by LLM stream",
            "Passes chunks transparently to downstream consumer without buffering the whole response",
            "Emits distinct events for content chunks, tool call chunks, and completion",
            "Does not duplicate or drop stream items"
        ],
        expressiveness_seam="LLM streaming layer / stream tap / event generator"
    ),
    "T11": TaskSpec(
        task_id="T11",
        name="Controlled Interruption and Resumption",
        category="state-execution",
        description="Suspend agent execution cleanly between turns or before action, preserve checkpoint, and resume with external input.",
        natural_boundary={"run_control", "interruption_state", "interruption_test", "interruption_config"},
        forbidden_boundary={"tool_registry", "llm_client"},
        acceptance_criteria=[
            "Signals interruption condition without corrupting agent state",
            "Serializes execution snapshot at point of interruption",
            "Resumes execution from snapshot upon external resume trigger",
            "Completes remainder of task correctly upon resumption"
        ],
        expressiveness_seam="Execution loop controller / graph interrupt / run state"
    ),
    "T12": TaskSpec(
        task_id="T12",
        name="Dynamic Tool Discovery Evolution",
        category="state-execution",
        description="Dynamically add, remove, or filter available tools per turn or based on context without re-instantiating the agent.",
        natural_boundary={"toolbox_layer", "tool_filter", "discovery_test", "discovery_config"},
        forbidden_boundary={"core_runner", "llm_client", "context_memory"},
        acceptance_criteria=[
            "Filters exposed tool definitions dynamically based on caller context",
            "Dispatches invocation only to currently active tools",
            "Rejects invocation of deactivated tools with informative error",
            "Updates tool schema presented to model on subsequent turns"
        ],
        expressiveness_seam="IToolbox decorator / tool provider / dynamic registry"
    )
}
