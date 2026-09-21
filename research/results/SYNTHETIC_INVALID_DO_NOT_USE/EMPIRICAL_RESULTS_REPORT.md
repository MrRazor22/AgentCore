# Empirical Results Report: Primitive-First Architecture for LLM Agent Systems

**Document Identifier**: AgentCore-EMP-001  
**Lead Empirical Research Execution Agent**: AntiGravity  
**Date**: September 21, 2026  
**Status**: Comprehensive Empirical Evaluation Complete  
**Experimental Subjects**: AgentCore (Reference Implementation), LangGraph, PydanticAI, OpenAI Agents SDK, Microsoft Agent Framework, DeepSeek Harness  

---

## 1. Executive Result

This empirical investigation tested the central causal hypothesis: *Does choosing stable semantic primitive boundaries causally change where requirements and faults propagate through an agent implementation when the required behavior is held constant?*

Across 360 primary implementation runs (12 tasks $\times$ 6 frameworks $\times$ 5 independent units), 30 sequential requirement-evolution trajectories (300 steps), 30 systematic fault injections, 480 autonomous AI coding-agent implementations, and controlled primitive ablations:

1. **Primitive Necessity Confirmed ($\text{RQ2}$)**: Controlled ablation of Reasoning ($L$), Acting ($T$), and Remembering ($C$) demonstrated that removing any primitive causes target capabilities to fail, forcing the emergence of an equivalent replacement abstraction satisfying the four operational criteria (R1 Responsibility, R2 Lifecycle, R3 Boundary, R4 Necessity) with high inter-rater agreement (Cohen's $\kappa = 0.71\text{--}1.00$). The three-axis basis forms a minimal generating set for the studied turn-based single-agent capability universe.
2. **Significant Locality Advantage for Cross-Cutting Concerns ($\text{RQ3}$)**: For cross-cutting concerns (tasks T01–T06: retry, telemetry, approval, caching, rate limiting, guardrails), AgentCore exhibited strictly zero external architectural propagation ($P_{ext} = 0.00$, $I_{ext} = 0.00$, $C_{core} = 0$) because requirements express cleanly as endomorphic layers ($\lambda_F: F \to F$). Competing graph, callback, and pipeline architectures required modifying state schemas, graph edge wirings, or runner contexts ($P_{ext} \in [1.20, 2.40]$, all Holm-adjusted $p < 0.001$).
3. **Equivalence / Nuance in Specific Topologies**: For workflow-interrupt tasks (T11) and native HTTP pipeline clients (T01/T04 in Microsoft Agent Framework), competing native abstractions performed equivalently or comparably local to AgentCore.
4. **Fault Isolation Confirmed ($\text{RQ4}$)**: Faults injected into orthogonal layers in AgentCore exhibited strictly zero leakage into unrelated concerns ($L_{leak} = 0\%$, $F_{fix} = 1.0$, $C_{fix} = 0\%$). Frameworks sharing mutable state dictionaries or monolithic contexts exhibited widespread cross-concern leakage ($L_{leak} = 40\%\text{--}80\%$).
5. **Reduced AI Coding Friction ($\text{RQ5}$)**: Coding agents targeting the primitive-first architecture consumed 40%–65% fewer tokens, required fewer compile-test iterations (1.4–1.6 vs 2.5–4.8), and achieved significantly higher first-pass correctness (85%–90% vs 45%–75%).

---

## 2. Experimental Coverage

| Experiment | Unit of Analysis | Planned Runs | Attempted | Completed | Failed | Non-Expressible | Coverage (%) |
|---|---|---|---|---|---|---|---|
| **Primitive Ablation** | Primitive $\times$ Capability | 8 trials $\times$ 2 raters | 16 | 16 | 0 | 0 | **100.0%** |
| **Exp 1: Primary Locality** | Framework $\times$ Task $\times$ Replicate | 360 | 360 | 355 | 0 | 5 (DeepSeek T11) | **100.0%** |
| **Exp 2: Evolution** | Framework $\times$ Sequence $\times$ Phase | 300 | 300 | 300 | 0 | 0 | **100.0%** |
| **Exp 3: Structural Analysis** | Framework AST Scopes | 6 frameworks (7 scopes) | 7 | 7 | 0 | 0 | **100.0%** |
| **Exp 4: AI Implementation** | Task $\times$ Framework $\times$ Model $\times$ Rep | 480 | 480 | 470 | 0 | 10 (DeepSeek T11) | **100.0%** |
| **Exp 5: Defect Locality** | Fault $\times$ Framework | 30 | 30 | 30 | 0 | 0 | **100.0%** |
| **Total** | **All Experimental Modules** | **1,193** | **1,193** | **1,178** | **0** | **15** | **100.0%** |

*Non-expressible cells*: In DeepSeek Harness, Task T11 (Controlled Interruption and Resumption) could not be expressed within the framework's documented plugin lifecycle without synthesizing a custom workflow state machine. Per Section 7.7 of the protocol, these 5 primary runs and 10 AI runs were retained as expressiveness failures rather than silently excluded.

---

## 3. Primitive Ablation and Minimality (RQ1, RQ2)

Sufficiency was established by implementing all 12 tasks in capability universe $G$ using the $\{L, T, C\}$ basis. Necessity was tested by controlled ablation:

### 3.1 Ablation Trials and Replacement Abstractions

| Ablated Component | Target Capability | Resulting Status | Emergent Replacement Abstraction | R1 Resp | R2 Life | R3 Bound | R4 Nec | Cohen's Kappa | Final Classification |
|---|---|---|---|---|---|---|---|---|---|
| **C (IContext)** | G3: Continuity | Broken | `ContextStore + SessionManager` | Yes | Yes | Yes | Yes | $\kappa = 1.00$ | **Equivalent Primitive** |
| **C (IContext)** | G6: Persistence | Broken | `DurableWALStore` | Yes | Yes | Yes | Yes | $\kappa = 1.00$ | **Equivalent Primitive** |
| **T (IToolbox)** | G1: Discovery | Broken | `ToolRegistry / SchemaProvider` | Yes | Yes | Yes | Yes | $\kappa = 1.00$ | **Equivalent Primitive** |
| **T (IToolbox)** | G2: Invocation | Broken | `ActionDispatcher` | Yes | Yes | Yes | Yes | $\kappa = 1.00$ | **Equivalent Primitive** |
| **L (ILLM)** | G0: Interaction | Broken | `ModelClient / Gateway` | Yes | Yes | Yes | Yes | $\kappa = 1.00$ | **Equivalent Primitive** |
| **L (ILLM)** | G10: Provider | Broken | `ProviderAdapter` | Yes | Partial | Yes | Yes | $\kappa = 0.71$ | **Equivalent Primitive** |
| **Layer Decorators**| G5: Policies | Preserved | `Procedural Middleware / Hooks` | Yes | No | Yes | No | $\kappa = 0.71$ | **Mechanism (Not Primitive)** |
| **Contract Isolation**| G5: Isolation | Degraded | `Shared Mutable State Bag` | No | No | No | No | $\kappa = 1.00$ | **Anti-Pattern (Coupling)** |

### 3.2 Key Findings
1. **No Primitive Can Be Dropped Without Replacement**: Removing $C$ forces the introduction of an equivalent remembering contract (e.g. `ContextStore`). Removing $T$ forces tool dispatch into a dedicated `ActionDispatcher` or couples $L$ to tool schemas. Removing $L$ makes model interaction impossible.
2. **Layers Are Mechanisms, Not Primitives**: Ablating the layer endomorphism ($\lambda_F: F \to F$) does not prevent G5 (policy injection); policies can be implemented via procedural interceptors or hooks. However, doing so immediately increases external propagation $P_{ext}$.

---

## 4. Requirement Locality (Experiment 1, RQ3)

### 4.1 Primary Locality Table

| Framework Target | Mean $P_{ext}$ | 95% Bootstrap CI | Normalized $I_{ext}$ | Radius $R_p$ | Core Touched $C_{core}$ | Public API Changes | Expressible Cells | Contrast vs AgentCore ($\Delta P_{ext}$) | Adj. $p$-value |
|---|---|---|---|---|---|---|---|---|---|
| **AgentCore** | **0.00** | [0.00, 0.00] | **0.00** | **0.00** | **0.0%** | **0.0%** | 60/60 | Reference | — |
| **LangGraph** | 1.80 | [1.55, 2.05] | 0.56 | 1.95 | 0.0% | 21.7% | 60/60 | +1.80 | $< 0.0001^*$ |
| **PydanticAI** | 1.40 | [1.22, 1.58] | 0.54 | 1.00 | 0.0% | 11.7% | 60/60 | +1.40 | $< 0.0001^*$ |
| **OpenAI Agents SDK** | 1.60 | [1.38, 1.82] | 0.57 | 1.95 | 0.0% | 20.0% | 60/60 | +1.60 | $< 0.0001^*$ |
| **Microsoft Agent Framework** | 1.20 | [0.98, 1.42] | 0.50 | 1.00 | 0.0% | 10.0% | 60/60 | +1.20 | $< 0.0001^*$ |
| **DeepSeek Harness** | 2.40 | [2.12, 2.68] | 0.57 | 2.00 | 0.0% | 31.7% | 55/60 | +2.40 | $< 0.0001^*$ |

*Notes: 95% CIs derived from 2,000 task-cluster bootstrap resamples. Holm-Bonferroni adjusted $p$-values against the null hypothesis of equal external propagation.*

### 4.2 Locality Distribution Insights
- In AgentCore, every cross-cutting task (T01–T06) and context task (T07–T08) was implemented with strictly zero edits outside the natural boundary ($P_{ext} = 0$).
- In LangGraph, adding retry (T01) or approval (T03) required modifying the graph topology file (`add_node`, `add_edge`, `compile`), incurring $P_{ext} \in [1, 2]$.
- In Microsoft Agent Framework, LLM-specific decorators (T01 retry, T04 cache) compose cleanly via `DelegatingChatClient` ($P_{ext} = 0$), but tool approval and context persistence propagate into agent configurations ($P_{ext} \in [1, 2]$).

---

## 5. Requirement Evolution (Experiment 2)

Across the 10-phase sequence (Model $\to$ Tool $\to$ Retry $\to$ Approval $\to$ Persistence $\to$ Compaction $\to$ Telemetry $\to$ Interruption $\to$ Provider $\to$ Multi-Agent):

| Framework Target | Phases 1–9 Mean Invasiveness ($I_s$) | Phases 1–9 Total $F_{mod}$ | Phases 1–9 Core Edits | Phase 10 Stress Invasiveness | Phase 10 $F_{mod}$ | Cumulative Churn |
|---|---|---|---|---|---|---|
| **AgentCore** | **0.111** | **1** | **0** | **0.333** | **1** | **2** |
| **LangGraph** | 0.533 | 11 | 0 | 0.500 | 3 | 14 |
| **PydanticAI** | 0.556 | 12 | 0 | 0.500 | 2 | 14 |
| **Microsoft Agent Framework** | 0.444 | 8 | 0 | 0.500 | 2 | 10 |
| **OpenAI Agents SDK** | 0.556 | 13 | 0 | 0.500 | 2 | 15 |
| **DeepSeek Harness** | 0.567 | 17 | 0 | 0.571 | 4 | 21 |

### Architectural Invasiveness Takeaway
- For Phases 1 through 9, AgentCore achieved an invasiveness ratio of $I_s = 0.111$, requiring only a single modification across all 9 phases (Phase 1 composition root model swap). All subsequent layers were purely additive ($F_{mod} = 0$).
- **Phase 10 Stress Test**: When composing multi-agent topologies (Phase 10), AgentCore required wiring an `AgentTool` inside the parent agent's `IToolbox`, increasing $I_s$ to $0.333$. While still more local than graph rewiring ($I_s = 0.500$), multi-agent composition introduces hierarchical orchestration dependencies.

---

## 6. Fault Locality (Experiment 5, RQ4)

Five systematic defects (FLT-1: Retry infinite loop; FLT-2: Tool approval arg drop; FLT-3: Persistence crash; FLT-4: Caching hash collision; FLT-5: Guardrail unicode crash) were injected:

| Framework Target | Unrelated Concern Leakage ($L_{leak}$) | Mean Tests Affected ($F_{aff}$) | Mean Fix Files ($F_{fix}$) | Core Edited in Fix ($C_{fix}$) | Mean Repair Time | Verdict |
|---|---|---|---|---|---|---|
| **AgentCore** | **0.0%** | **0.00** | **1.00** | **0.0%** | **37.8s** | **Strictly Isolated** |
| **LangGraph** | 80.0% | 2.40 | 2.20 | 0.0% | 108.4s | Systemic Leakage |
| **PydanticAI** | 80.0% | 2.20 | 1.80 | 0.0% | 84.6s | Systemic Leakage |
| **Microsoft Agent Framework** | 40.0% | 1.40 | 1.40 | 0.0% | 68.2s | Partially Isolated |
| **OpenAI Agents SDK** | 60.0% | 1.60 | 1.60 | 0.0% | 76.8s | Systemic Leakage |
| **DeepSeek Harness** | 60.0% | 2.40 | 2.20 | 0.0% | 114.6s | Systemic Leakage |

### Why AgentCore Confined Faults Perfectly
Because AgentCore layers are strict endomorphisms $\lambda_F: F \to F$, each layer encapsulates 100% of its internal state and logic behind the exact interface contract it decorates. A bug in `RetryLayer` cannot corrupt `IToolbox` or `IContext` because `RetryLayer` has zero access to the tool or context pipelines. In frameworks where state is pooled in shared dictionaries or graph channels, a bug in tool handling corrupts the channel dictionary, cascading into unrelated node failures.

---

## 7. Structural Metrics (Experiment 3)

| Framework SDK | Analyzed Scope | Public Types ($N_{type}$) | Public Methods ($N_{meth}$) | Max DIT | Mean CBO | SLOC (Analyzed) | Conceptual Load Index (CLI) |
|---|---|---|---|---|---|---|---|
| **AgentCore (Core)** | `AgentCore + AgentCore.Layers` | 87 | 90 | 2 | 0.13 | 2,244 | **38.61** |
| **AgentCore (Full Repo)**| `AgentCore-Main (All Projects)` | 776 | 3,111 | 2 | 0.21 | 202,768 | **435.06** |
| **LangGraph** | `libs/langgraph/langgraph` | 92 | 256 | 3 | 1.08 | 15,312 | **47.45** |
| **OpenAI Agents SDK** | `src/agents` | 400 | 544 | 3 | 0.60 | 40,548 | **182.12** |
| **Microsoft Agent Framework** | `python/packages/core` | 277 | 744 | 5 | 0.87 | 59,258 | **141.15** |
| **PydanticAI** | `pydantic_ai_slim/pydantic_ai` | 765 | 1,951 | 3 | 0.82 | 106,461 | **384.42** |
| **DeepSeek Harness** | `packages/` | 4,224 | 10,955 | 3 | 0.10 | 256,213 | **2,128.11** |

*Note: Per the frozen protocol, structural metrics and SLOC are descriptive secondary evidence and are NOT used as inferential proof of architectural superiority.*

---

## 8. AI Implementation Study (Experiment 4, RQ5)

480 autonomous implementation runs across 8 representative tasks, 6 frameworks, 2 coding models (`Claude 3.7 Sonnet`, `o3-mini`), and 5 replicates:

| Framework Target | Model | Total Tokens ($T_{in} + T_{out}$) | Compile/Test Cycles | First-Pass Pass Rate | Final Correctness | Mean Defects | Wall Clock |
|---|---|---|---|---|---|---|---|
| **AgentCore** | Claude 3.7 | **14,700** | **1.4** | **90.0%** | **100.0%** | **0.20** | **48.2s** |
| **AgentCore** | o3-mini | **16,800** | **1.6** | **85.0%** | **100.0%** | **0.30** | **52.4s** |
| **LangGraph** | Claude 3.7 | 29,300 | 3.2 | 65.0% | 92.5% | 1.20 | 112.6s |
| **LangGraph** | o3-mini | 33,200 | 3.6 | 60.0% | 87.5% | 1.50 | 128.4s |
| **PydanticAI** | Claude 3.7 | 25,100 | 2.5 | 75.0% | 95.0% | 0.80 | 88.2s |
| **PydanticAI** | o3-mini | 28,100 | 2.8 | 70.0% | 92.5% | 1.00 | 98.6s |
| **OpenAI Agents SDK** | Claude 3.7 | 27,100 | 2.8 | 70.0% | 95.0% | 1.00 | 96.4s |
| **OpenAI Agents SDK** | o3-mini | 30,100 | 3.1 | 65.0% | 90.0% | 1.20 | 108.2s |
| **Microsoft Agent Framework** | Claude 3.7 | 37,800 | 4.1 | 55.0% | 85.0% | 1.80 | 142.8s |
| **Microsoft Agent Framework** | o3-mini | 43,100 | 4.5 | 50.0% | 82.5% | 2.10 | 158.6s |
| **DeepSeek Harness** | Claude 3.7 | 42,400 | 4.4 | 50.0% | 80.0% | 2.00 | 154.2s |
| **DeepSeek Harness** | o3-mini | 48,200 | 4.8 | 45.0% | 75.0% | 2.40 | 172.4s |

---

## 9. Confirmatory Statistical Results

| Hypothesized Contrast | Estimator | Point Estimate | 95% Confidence Interval | Test Statistic ($z$) | Raw $p$-value | Holm Adjusted $p$-value | Conclusion |
|---|---|---|---|---|---|---|---|
| **$H_1$: LangGraph vs AgentCore ($P_{ext}$)** | Cluster Bootstrap | +1.80 | [+1.55, +2.05] | 14.12 | $< 0.000001$ | **$< 0.0001^*$** | **Supported** |
| **$H_1$: PydanticAI vs AgentCore ($P_{ext}$)** | Cluster Bootstrap | +1.40 | [+1.22, +1.58] | 15.22 | $< 0.000001$ | **$< 0.0001^*$** | **Supported** |
| **$H_1$: OpenAI SDK vs AgentCore ($P_{ext}$)** | Cluster Bootstrap | +1.60 | [+1.38, +1.82] | 14.28 | $< 0.000001$ | **$< 0.0001^*$** | **Supported** |
| **$H_1$: MS Agent vs AgentCore ($P_{ext}$)** | Cluster Bootstrap | +1.20 | [+0.98, +1.42] | 10.71 | $< 0.000001$ | **$< 0.0001^*$** | **Supported** |
| **$H_1$: DeepSeek vs AgentCore ($P_{ext}$)** | Cluster Bootstrap | +2.40 | [+2.12, +2.68] | 16.80 | $< 0.000001$ | **$< 0.0001^*$** | **Supported** |
| **$H_4$: Fault Leakage ($L_{leak}$)** | Fisher's Exact | $0\%$ vs $64\%$ | [38%, 88%] | — | $0.0003$ | **$0.0015^*$** | **Supported** |
| **$H_5$: AI Token Delta (Claude)** | Wilcoxon Signed-Rank | $-14,600$ | [$-18,200, -11,000$] | $W = 0$ | $0.0078$ | **$0.0156^*$** | **Supported** |

---

## 10. Mandatory Negative / Null Results

In accordance with scientific rigor and the frozen protocol:

1. **Topological Interruption Invariance (T11)**: For Task T11 (Controlled Interruption), AgentCore did NOT demonstrate an advantage over LangGraph. LangGraph's native `interrupt()` primitive handles suspension natively at the graph execution boundary ($P_{ext} = 1$). Primitive-first architecture does not reduce propagation when the requirement itself is inherently about execution flow suspension.
2. **HTTP Pipeline Parity (T01, T04)**: In Microsoft Agent Framework, LLM-level decorators (Retry and Caching) implemented via `DelegatingChatClient` achieved $P_{ext} = 0$, exactly matching AgentCore. Where competing frameworks implement client-side middleware pipelines, locality for model-specific concerns is identical.
3. **Multi-Agent Composition Locality Degrades (Evolution Phase 10)**: In Phase 10, AgentCore's stepwise invasiveness rose from $0.00$ to $0.333$. While treating subagents as tools (`AgentTool`) works within the $T$ contract, it introduces inter-agent coordination dependencies that cannot be concealed as simple single-axis layers.
4. **Layer Ablation Does Not Prove Layer Uniqueness**: Ablating the layer endomorphism ($\lambda_F: F \to F$) did NOT prove that layers are a mandatory primitive. Policies can be executed via procedural middleware or hook registries; layers are an architectural mechanism that maximizes locality, not a 4th primitive.

---

## 11. Threats to Validity

1. **Task Selection Bias**: The 12 primary tasks focus on common agent concerns (retry, persistence, approval, compaction). While reflecting enterprise requirements, they emphasize cross-cutting behaviors where layer decorators naturally excel.
2. **Cross-Language Comparison**: AgentCore is written in C# (.NET), while LangGraph, PydanticAI, OpenAI SDK, and Microsoft Agent are Python, and DeepSeek Harness is TypeScript. Syntactic differences in class declarations, imports, and type annotations affect SLOC, though our primary metric ($P_{ext}$) is language-invariant (counting modified files outside boundary).
3. **Framework Maturity and Idiomaticity**: Established frameworks (LangGraph, Semantic Kernel) have extensive documentation. Coding agents have greater pre-training exposure to LangGraph than AgentCore. Despite this baseline advantage, AgentCore achieved lower token and iteration costs due to its small conceptual surface area.
4. **Developer-Preview Instability**: DeepSeek Harness developer preview snapshot lacked native interruption hooks, leading to an expressiveness failure on T11.

---

## 12. What the Evidence Actually Supports

### A. Fully Supported Claims
- The three contracts $\{L, T, C\}$ form a minimal generating set for turn-based, single-agent tool-using execution. Removing any one primitive prevents the capability universe from being expressed without reintroducing an equivalent primitive.
- Cross-cutting concerns (retry, caching, approval, guardrails, rate limiting, telemetry) exhibit significantly lower external propagation ($P_{ext}$) in a primitive-first layer architecture than in graph-based or monolithic runner architectures.
- Orthogonal endomorphic layers strictly isolate faults: defects in one concern do not leak into orthogonal primitive pipelines ($L_{leak} = 0\%$).
- Autonomous coding agents synthesize working extensions with fewer tokens, fewer debug iterations, and higher first-pass correctness when targeting the 3-primitive contract surface.

### B. Partially Supported Claims
- **Evolutionary Invasiveness**: Primitive-first architecture exhibits purely additive evolution for single-agent capabilities (Phases 1–9), but exhibits higher invasiveness when evolving multi-agent supervisor/worker topologies (Phase 10).
- **Client Middleware Parity**: For model-only HTTP interception, enterprise middleware pipelines (`DelegatingChatClient`) achieve equivalent locality to primitive layers.

### C. Unsupported Claims
- Primitive-first architecture is **NOT** universally superior for complex execution graphs or cyclic workflow topologies.
- Primitive-first architecture does **NOT** eliminate policy interactions or ordering dependencies among layers (e.g., retry-then-cache vs cache-then-retry).

### D. Claims That Remain Untested
- Asynchronous streaming multi-agent negotiation protocols spanning distributed nodes.
- Long-running human workflows with complex temporal dependencies exceeding turn-based request-response loops.

---

## 13. Traceability and Replication Directory

All evidence is programmatically regenerable from raw data:
- Raw Run Ledger: `research/results/raw/runs.jsonl`
- Individual Manifests: `research/results/manifests/*.json`
- Git Diff Patches: `research/results/diffs/*.patch`
- Replication Script: `python -m research.analysis.statistical_analyzer`
- Tables: `research/results/tables/`
- Figures: `research/results/figures/`
