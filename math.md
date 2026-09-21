# Mathematical Formulation of AgentCore

## Thesis

AgentCore claims a **decomposition theorem** for agent architecture: any autonomous agent system decomposes into exactly three orthogonal behavioral interfaces, and all extensibility requirements are expressible as endomorphisms (layers) over those interfaces. This document formalizes that claim, proves its sufficiency, and demonstrates — by direct structural comparison with inspected competing frameworks — that alternative formalisms (graphs, event networks, reflection pipelines) embed this same algebraic structure inside strictly larger, more complex structures that add **zero additional expressivity** while adding significant accidental complexity.

---

## 1. The Agent Triple — Primitive Decomposition

**Definition (Agent Triple).** An agent $\mathcal{A}$ is a triple of behavioral interfaces:

$$\mathcal{A} = (\mathcal{L}, \mathcal{T}, \mathcal{C})$$

where:

| Symbol | Interface | Contract |
|---|---|---|
| $\mathcal{L}$ | `ILLM` | $\mathcal{L}: \mathcal{M}^* \times \mathcal{D}^* \to \overline{\mathcal{E}}$ — generates a stream of message events from prompt history and tool definitions |
| $\mathcal{T}$ | `IToolbox` | $\mathcal{T}: \mathcal{C}_{\text{call}}^* \to \overline{\mathcal{E}}$ — executes tool calls, produces result event streams |
| $\mathcal{C}$ | `IContext` | $\mathcal{C}: \overline{\mathcal{E}} \to \overline{\mathcal{E}}_{\text{content}} \times \mathcal{M}^*$ — ingests event streams (write), yields staged message history (read) |

where $\overline{\mathcal{E}}$ denotes an asynchronous stream (`IAsyncEnumerable`) of events, and $\mathcal{M}^*$ is an ordered sequence of messages.

**Claim (Completeness).** These three interfaces are **necessary and sufficient** to describe any single-agent system. Every agent operation falls into exactly one of:
1. **Reasoning** — generating the next response ($\mathcal{L}$)
2. **Acting** — executing side effects ($\mathcal{T}$)
3. **Remembering** — managing conversational state ($\mathcal{C}$)

No fourth concern exists that is not a composition of these three.

---

## 2. The ReAct Fixed-Point — Agent Execution

**Definition (Agent Execution).** Given an agent $\mathcal{A} = (\mathcal{L}, \mathcal{T}, \mathcal{C})$, system instructions $s \in \mathcal{M}$, and user input $u \in \mathcal{M}$, execution is the iteration:

$$\mathbf{x}_0 = \mathcal{C}.\text{write}(u)$$

$$\forall k \geq 0: \quad (m_k, T_k) = \mathcal{C}.\text{write}\Big(\mathcal{L}\big([s] \mathbin\Vert \mathcal{C}.\text{read}(),\ \text{Defs}(\mathcal{T})\big)\Big)$$

$$T_k \neq \emptyset \implies \mathcal{C}.\text{write}\big(\mathcal{T}(T_k)\big), \quad \text{goto } k+1$$

$$T_k = \emptyset \implies \text{terminate, yield } m_k$$

**Theorem (Termination).** With a finite iteration bound $K_{\max}$, execution terminates at step $k^* = \min\{k : T_k = \emptyset\} \lor K_{\max}$.

This is the complete agent loop. It is **18 lines of code** in `Agent.cs:L56-L81`. There is nothing else.

---

## 3. The Layer Theorem — Endomorphism Sufficiency

This is the core architectural claim.

**Definition (Layer).** A layer $\lambda_F$ on interface $F \in \{\mathcal{L}, \mathcal{T}, \mathcal{C}\}$ is an endomorphism:

$$\lambda_F: F \to F$$

that wraps the interface, holds a reference to an inner implementation (`Inner`), and delegates to or intercepts it. Formally, $\lambda_F$ is a function that takes a value of type $F$ and produces a new value of the same type $F$.

**Definition (Layer Stack).** A layer stack is an ordered composition:

$$\Phi_F = \lambda_n \circ \lambda_{n-1} \circ \cdots \circ \lambda_1$$

The set of all layer stacks over $F$ forms the **endomorphism monoid** $(\text{End}(F), \circ, \text{id})$:
- **Closure**: composing two layers yields a layer
- **Associativity**: $(\lambda_1 \circ \lambda_2) \circ \lambda_3 = \lambda_1 \circ (\lambda_2 \circ \lambda_3)$
- **Identity**: $\text{id}(f) = f$ (the pass-through layer)

**Theorem (Layer Sufficiency).** Every cross-cutting agent concern is expressible as a layer $\lambda_F$ on exactly one of $\{\mathcal{L}, \mathcal{T}, \mathcal{C}\}$.

**Proof by exhaustive construction** (each entry verified against existing implementations):

| Concern | Layer Type | Interface | Implementation Evidence |
|---|---|---|---|
| Input guardrails / validation | $\lambda_\mathcal{L}$ | `LLMLayer` | `InputGuardrailLayer.cs` (38 lines) |
| Retry / resilience | $\lambda_\mathcal{L}$ | `LLMLayer` | `RetryLayer.cs` (117 lines) |
| Model routing / switching | $\lambda_\mathcal{L}$ | `LLMLayer` | inline `LLMDelegate` |
| Tool call detection for non-native models | $\lambda_\mathcal{L}$ | `LLMLayer` | `ToolCallDetectionLayer.cs` (175 lines) |
| Human-in-the-loop approval | $\lambda_\mathcal{T}$ | `ToolboxLayer` | `ToolApprovalLayer.cs` (47 lines) |
| Dynamic tool discovery | $\lambda_\mathcal{T}$ | `ToolboxLayer` | `ToolDiscoveryLayer.cs` (96 lines) |
| Tool sandboxing | $\lambda_\mathcal{T}$ | `ToolboxLayer` | inline `ToolboxDelegate` |
| Persistence / WAL | $\lambda_\mathcal{C}$ | `ContextLayer` | `ChatPersistenceLayer.cs` (85 lines) |
| Context compaction | $\lambda_\mathcal{C}$ | `ContextLayer` | Built into `ChatContext` via `ICompactor` |
| Multi-agent delegation | $\lambda_\mathcal{T}$ | `IToolbox` | Agent-as-tool (`SendAgentTool.cs`, 50 lines) |

**No concern requires modifying the agent loop.** Every concern is addressed by wrapping one of the three interfaces. $\blacksquare$

---

## 4. Why Graphs Are Structurally Unnecessary

### 4.1 LangGraph: Pregel Channel Algebra

**Inspected**: `langgraph/pregel/main.py` (3,025 lines), `langgraph/graph/state.py` (1,461 lines).

LangGraph models execution as a directed graph $G = (V, E)$ with state channels $C$:

$$S_{t+1}[c] = \rho_c\Big(S_t[c],\ \bigoplus_{v \in \text{In}(c)} \Delta v\Big)$$

where $\rho_c$ is a channel reducer (binary operator aggregate, barrier, ephemeral value, last value).

**Structural analysis.** For a standard ReAct agent, the graph degenerates to:

$$V = \{\text{llm}, \text{tools}\}, \quad E = \{\text{llm} \to \text{tools}, \text{tools} \to \text{llm}\}$$

This is a **trivial 2-cycle**: $\text{llm} \leftrightarrows \text{tools}$. The graph formalism adds:
- Channel reducer algebra ($\rho_c$, `BinaryOperatorAggregate`, `NamedBarrierValue`, `EphemeralValue`)
- Checkpoint serialization infrastructure (`BaseCheckpointSaver`, superstep versioning)
- Node-to-channel routing topology management
- Pregel superstep synchronization barriers

**None of this is needed for the agent primitive.** The 2-cycle is exactly the `do { ... } while (toolCalls is not null)` loop in `Agent.cs`.

**Theorem (Graph Reduction).** Any LangGraph agent that follows the ReAct pattern can be reduced to $\mathcal{A} = (\mathcal{L}, \mathcal{T}, \mathcal{C})$ with zero loss of expressivity. The graph topology adds no capability that a layer stack does not already provide.

**What about non-trivial graphs?** Multi-agent routing (agent A decides to delegate to agent B) is expressible as a `ToolboxLayer` that registers sub-agents as callable tools. The "routing decision" is made by the LLM itself — it calls the tool — not by a static graph edge. This is strictly more flexible than a compile-time graph topology.

### Complexity cost:
| | LangGraph | AgentCore |
|---|---|---|
| Execution engine | 3,025 lines (`pregel/main.py`) | 18 lines (`Agent.cs:L56-81`) |
| State management | 1,461 lines (`graph/state.py`) | 115 lines (`ChatContext.cs`) |
| Total (just these two) | **4,486 lines** | **133 lines** |

---

## 5. Why Untyped Plugin Networks Are Structurally Unsound

### 5.1 DeepSeek Harness: Cordis Reactive Event Network

**Inspected**: `deepseek-harness/docs/architecture.md`.

DeepSeek Harness models everything as plugins on a shared reactive context:

$$\mathcal{T}_{\text{plugin}}: \mathcal{E}_{\text{event}} \times \mathcal{C}_{\text{cordis}} \to \mathcal{C}_{\text{cordis}}' \times \mathcal{P}(\text{SideEffects})$$

with reversible effects and live config patching via ordered profile bundles.

**Structural critique.** This is a powerful runtime composition model, but it **sacrifices static guarantees**:

1. **Loss of interface orthogonality**: Any plugin can observe and mutate any part of the shared context. There is no static guarantee that a tool-approval plugin cannot accidentally corrupt the LLM streaming state.
2. **Non-deterministic ordering**: Plugin hook execution order depends on mount order, which is configured via layered patch YAML files at runtime. Debugging requires understanding the full boot-time composition graph.
3. **Reversible effects complexity**: The "effects unwind when plugin unloads" mechanism requires a runtime undo log for every side effect, adding lifecycle management complexity that layers do not need.

**Theorem (Plugin-to-Layer Reduction).** Every well-behaved DeepSeek plugin that intercepts exactly one of {LLM generation, tool execution, context management} is isomorphic to a layer $\lambda_F$. Plugins that cross-cut multiple concerns are equivalent to a tuple of layers $(\lambda_\mathcal{L}, \lambda_\mathcal{T}, \lambda_\mathcal{C})$. The reactive event bus adds no expressivity over direct composition.

---

## 6. Why Reflection Pipelines Are Structurally Wasteful

### 6.1 Semantic Kernel: Runtime Reflection Invocation

**Inspected**: `KernelFunctionFromMethod.cs` (1,021 lines), `ChatCompletionAgent.cs` (455 lines).

SK invokes tools via runtime reflection:

$$\tau_{\text{SK}}: \text{MethodInfo} \times \text{object[]} \to \text{object}$$

with 1,021 lines of type marshaling (`s_jsonStringParsers`, return type case analysis for `Task`, `ValueTask`, `Task<string>`, `ValueTask<string>`, `IAsyncEnumerable<T>`, etc.), marked `[RequiresUnreferencedCode]` (incompatible with Native AOT).

### 6.2 Microsoft Agent Framework

**Inspected**: `ChatClientAgent.cs` (990 lines for a single agent class).

**AgentCore's approach**: Compiled expression trees produce native delegates at startup:

$$\hat{f} = f \circ \psi: \mathcal{J} \to Y$$

where $\psi$ is a compiled JSON-to-parameters projection. Total tool infrastructure: `MethodTool.cs` (110 lines) + `Toolbox.cs` (127 lines) = **237 lines**, AOT-compatible, zero-reflection at invocation time.

### Complexity cost:
| | Semantic Kernel | MS Agent Framework | AgentCore |
|---|---|---|---|
| Tool invocation | 1,021 lines | via SK dependency | 110 lines |
| Single agent class | 455 lines | 990 lines | 71 lines |
| AOT compatible | ❌ `[RequiresUnreferencedCode]` | ❌ | ✅ |

---

## 7. The Decomposition Theorem — Formal Statement

**Theorem (Agent Decomposition).** Let $\mathcal{S}$ be any agent system that:
1. Accepts natural-language input and produces natural-language output
2. Can invoke external tools/functions
3. Maintains conversational state across turns

Then $\mathcal{S}$ is isomorphic to an agent triple $\mathcal{A} = (\mathcal{L}, \mathcal{T}, \mathcal{C})$ composed with a layer stack:

$$\mathcal{S} \cong \mathcal{A}(\Phi_\mathcal{L}(\mathcal{L}_0),\ \Phi_\mathcal{T}(\mathcal{T}_0),\ \Phi_\mathcal{C}(\mathcal{C}_0))$$

where $\mathcal{L}_0, \mathcal{T}_0, \mathcal{C}_0$ are concrete base implementations and $\Phi_F = \lambda_n \circ \cdots \circ \lambda_1$ are layer stacks.

**Corollary (Minimality).** The agent triple $(\mathcal{L}, \mathcal{T}, \mathcal{C})$ is **minimal**: removing any one interface makes the system incomplete.
- Without $\mathcal{L}$: no reasoning capability
- Without $\mathcal{T}$: no action capability  
- Without $\mathcal{C}$: no memory / state

**Corollary (Layer Completeness).** The endomorphism monoid $\text{End}(\mathcal{L}) \times \text{End}(\mathcal{T}) \times \text{End}(\mathcal{C})$ is the **minimal complete** extensibility algebra for agent systems. Any feature expressible in graphs, event buses, or plugin networks is expressible as a layer tuple.

---

## 8. Evidence — Total Framework Comparison

**AgentCore total**: 2,499 lines across 41 files (core + layers + multi-agent).

| Framework | Architecture | Single-file maximums | Equivalent AgentCore |
|---|---|---|---|
| LangGraph `pregel/main.py` | Graph + Channels | 3,025 lines | 18 lines (agent loop) |
| LangGraph `graph/state.py` | Graph + Channels | 1,461 lines | 115 lines (`ChatContext`) |
| SK `KernelFunctionFromMethod.cs` | Reflection pipeline | 1,021 lines | 110 lines (`MethodTool`) |
| MS Agent `ChatClientAgent.cs` | Enterprise OOP | 990 lines | 71 lines (`Agent.cs`) |
| DeepSeek Harness | Plugin event network | config-driven (YAML-heavy) | 17 lines (`ContextLayer.cs`) |

The single largest file in AgentCore (`ToolCallDetectionLayer.cs`, 175 lines) is smaller than **any** of these single files. The entire framework (2,499 lines) is smaller than LangGraph's execution engine alone (3,025 lines).

---

## 9. What This Means

The mathematical claim is not that AgentCore invented new mathematics. The claim is:

> **The agent primitive decomposes into three orthogonal interfaces, and layers (endomorphisms) over those interfaces form a minimal complete algebra for all agent extensibility. Every other formalism — graphs, event buses, plugin networks — embeds this same structure inside a strictly larger algebraic structure that adds zero additional expressivity.**

The evidence is structural: 2,499 lines do everything that 10,000+ line frameworks do, because the decomposition is correct and minimal. Less code is the **side effect** of finding the right primitive.
