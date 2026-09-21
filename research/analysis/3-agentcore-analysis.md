# AgentCore Architecture Analysis (Research Artifact)

> This document analyzes AgentCore as a research artifact — an implementation of the primitive-first design hypothesis. It does NOT advocate for AgentCore. It documents the architectural decisions, the claimed primitive decomposition, and the areas where the design may be incomplete or questionable.

---

## 1. The Claimed Primitive: The Agent Triple

AgentCore decomposes an agent into three behavioral interfaces:

| Interface | Responsibility | Contract (simplified) |
|---|---|---|
| `ILLM` | Reasoning | messages + tool definitions → event stream |
| `IToolbox` | Acting | tool calls → result event stream |
| `IContext` | Remembering | event stream ↔ message history |

### 1.1 What Each Interface Actually Does

**`ILLM`** (16 lines): A single method `GenerateAsync` that takes messages, tool definitions, an optional response schema, and returns `IAsyncEnumerable<IMessageEvent>`. It is strictly a streaming generation contract. It does NOT know about tools, context, or agent state.

**`IToolbox`** (contract + implementation ~278 lines): Registers tools. When given tool call events, executes them in parallel via `Parallel.ForEachAsync`. Returns result event streams. It does NOT know about the LLM or context.

**`IContext`** (contract + implementation ~206 lines): Accepts streaming events via `WriteAsync`, assembles them into complete messages, and returns message history via `ReadAsync`. Optionally compacts history when it exceeds token limits. It does NOT know about the LLM or tools.

### 1.2 The Agent Loop

The agent loop (87 lines total, ~27 lines of core logic) consumes these three interfaces:

```
while (has tool calls):
    context.write(user input)
    events = llm.generate(context.read(), toolbox.definitions())
    context.write(events)
    if events contain tool calls:
        results = toolbox.execute(tool calls)
        context.write(results)
```

This is a direct implementation of the ReAct pattern. The loop has NOT been modified since the architecture stabilized.

### 1.3 Design Questions to Investigate

- **Is three interfaces the right number?** Could `IToolbox` be split into `IToolRegistry` + `IToolExecutor`? The current design merges registration and execution into one interface. Is that a simplification or a conflation?
- **Is `IContext` doing too much?** It handles: event stream assembly, message history storage, and compaction triggering. Are these genuinely one concern or three?
- **Why not two interfaces?** Some frameworks (e.g., Claude SDK) treat tool execution as part of the LLM call. AgentCore separates them. Is this decomposition justified or arbitrary?
- **Why not four?** Some frameworks separate "memory" from "history" (e.g., Letta with core memory + archival memory). AgentCore's `IContext` handles both. Is this insufficient?

---

## 2. The Layer Mechanism

Each interface has a corresponding layer base class:
- `LLMLayer` wraps `ILLM` (29 lines)
- `ToolboxLayer` wraps `IToolbox` (26 lines)
- `ContextLayer` wraps `IContext` (20 lines)

Layers are decorators. They hold a reference to an `Inner` implementation and can intercept, modify, or augment behavior.

### 2.1 Built Layers

| Layer | Wraps | Lines | Purpose |
|---|---|---|---|
| `RetryLayer` | `ILLM` | 138 | Exponential backoff for transient LLM errors |
| `InputGuardrailLayer` | `ILLM` | 38 | Policy check before LLM invocation |
| `ToolCallDetectionLayer` | `ILLM` | 201 | Extract tool calls from raw text for non-native-function-calling models |
| `ToolApprovalLayer` | `IToolbox` | 43 | Async approval gate before tool execution |
| `ToolDiscoveryLayer` | `IToolbox` | 113 | Meta-tool for dynamic tool search |
| `ChatPersistenceLayer` | `IContext` | 105 | WAL-based persistence with crash recovery |

### 2.2 Design Questions to Investigate

- **Is the decorator pattern the right choice?** Decorators require explicit wrapping. Is this better or worse than event hooks for typical cross-cutting concerns? Under what conditions?
- **Layer ordering matters.** `RetryLayer` wrapping `InputGuardrailLayer` behaves differently from the reverse. Is this an advantage (explicit control) or a problem (ordering bugs)?
- **Are there concerns that DON'T map to a single layer?** The current claim is that every cross-cutting concern maps to exactly one of the three interfaces. Is there a concern that genuinely cross-cuts two or more interfaces simultaneously?
- **Runtime add/remove**: Layers can be added/removed at runtime. Is this necessary? When would you dynamically add a layer mid-conversation?

---

## 3. Policies vs Primitives

AgentCore distinguishes between:
- **Primitives**: `ILLM`, `IToolbox`, `IContext` — the fundamental interfaces
- **Policies**: `ICompactor`, `IAssembler`, `ITool` — injectable behaviors within the primitives
- **Layers**: `LLMLayer`, `ToolboxLayer`, `ContextLayer` — decorators around the primitives

### 3.1 Examples of Policy Injection

- `ICompactor` injected into `ChatContext` determines when and how to compact history
- `ITool` implementations are registered with `IToolbox` — each tool is a policy
- `IAssembler` determines how streaming chunks become complete messages

### 3.2 Design Questions

- Is the distinction between "policy" and "layer" clear? Both are injectable. When should something be a policy vs a layer?
- Could `ICompactor` have been a `ContextLayer` instead? What's the design reason for it being an injected policy rather than a layer?

---

## 4. Content Model

AgentCore's content model defines event types:

- `Text` — textual content
- `Reasoning` — chain-of-thought content
- `ToolCall` — a request to invoke a tool
- `ToolResult` — the result of a tool invocation
- `Image` — image content (URL or bytes)
- `Audio` — audio content
- `Video` — video content

These are modeled as `IContent` records within `IContentEvent` message events.

### 4.1 Design Questions

- Is the content model part of the primitive or a policy? Currently it's a fixed type hierarchy. Would a more extensible content model be needed?
- The content types are multimodal but discrete. Real-time streaming media (voice, live video) is explicitly excluded. Is this a limitation or a correct boundary?

---

## 5. What AgentCore Does NOT Do

This is critical for honest analysis:

- **No declarative agent definition** — agents are constructed in code, not YAML/JSON
- **No auto-planning** — no built-in goal decomposition
- **No A2A protocol** — no inter-agent network protocol
- **No built-in connectors** — no database, API, or cloud service integrations
- **No embedded language execution** — no Python/JS sandboxing
- **No visual DevUI** — no debugging dashboard
- **No durable distributed execution** — no Temporal-style workflow orchestration

The claim is that none of these are architecturally blocked. Whether that claim is true is an empirical question.

---

## 6. Design Evolution (from developer's account)

The developer reports the design evolved through these stages:
1. Started as a monolithic agent
2. Separated LLM from toolbox
3. Separated context as memory
4. Tried multiple separate abstractions (registry, executor, etc.)
5. Architecture became increasingly complex
6. Pivoted to asking "what is the primitive?" instead of "what feature to add?"
7. Identified the three-interface decomposition
8. Features stopped fighting each other; layers became natural

This evolutionary narrative is important for the paper but needs to be presented as descriptive (what happened) not prescriptive (what should happen).

### 6.1 Questions for Investigation

- Can this evolutionary process be replicated? If another developer starts from scratch, do they arrive at the same primitive?
- Is the three-interface decomposition an artifact of the agent domain, or could it be different for a different type of agent?
- Was the "pivoting moment" genuinely about finding the primitive, or was it about the developer gaining enough domain experience to see the pattern?
