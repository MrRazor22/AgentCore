# FINAL ADVERSARIAL FORENSIC AUDIT: EMPIRICAL BENCHMARK EVALUATION

**Audit Date**: September 21, 2026  
**Target Repository Commit**: `30af63b923277d8cf7232c26d57f42afb0ee4fec`  
**Base Benchmark Commit**: `b11d0e489f08e1cc73284a1088e56d3720386597`  
**Auditor Persona**: Hostile External Peer Reviewer / Formal Methods Auditor  
**Audit Objective**: Attempt to falsify and disprove the empirical claims of Primitive-First Agent Architecture.

---

## EXECUTIVE VERDICT: **CATEGORY B**
### **EMPIRICALLY VALID BUT REQUIRES METHODOLOGICAL REVISION**

### Why Not Category A (Publication-Ready as-is)?
1. **Sample Count Truncation (Exp 1 Locality)**: The frozen theoretical protocol in `5-experiment-design.md` specified **360 planned cells** (12 tasks x 6 frameworks x 5 replicates). The executed physical runs comprise **37 physical runs** across 4 frameworks (AgentCore: 13, LangGraph: 13, PydanticAI: 6, OpenAI: 5). Two frameworks (Microsoft Agent Framework and DeepSeek Harness) were excluded from Locality tasks due to API non-expressiveness/access constraints, and replicates $R3..R5$ were not executed.
2. **Definitional Asymmetry in Natural Boundary $B(r)$**: In LangGraph and PydanticAI, idiomatic single-file application composition (e.g. `agent_graph.py`, `agent.py`) is standard tutorial practice. Under the benchmark's strict natural boundary protocol, editing `agent_graph.py` to add a retry policy or a node is classified as core runner leakage ($P_{\text{ext}} = 1$). While formally true under strict separation of concerns, a reviewer can legitimately argue that `agent_graph.py` is an application script, not the framework engine.
3. **Statistical Degeneracy from Bounded Metric**: $\Delta P_{\text{ext}} = 1.000$ with a 95% BCa CI of $[1.000, 1.000]$ occurs because $P_{\text{ext}}$ is a discrete binary indicator that was $0$ for all AgentCore runs and $1$ for all competitor runs. Claiming $p < 0.0001$ via non-parametric bootstrap over a degenerate binary distribution is technically an artifact of zero variance.

### Why Not Category C (Untrustworthy / Fake)?
1. **Zero Synthetic Generators**: All 37 task runs, the 10 evolution phases, and the 5 fault injection scenarios possess physical unit tests that compile and pass under `dotnet test` and `python -m unittest`.
2. **First-Principles Locality Reconciliation**: 100% of physical patches reconciled against the frozen natural boundary with **zero mismatches** (`reconstructed_locality.csv` match rate: 37/37 = 100%).
3. **Core Repository Invariance Confirmed**: `AgentCore/` and `AgentCore.Layers/` source code remained strictly unchanged ($F_{\text{core\_mod}} = 0$).

---

## 1. Artifact Inventory & Cryptographic Hashes
- All 37 task run manifests, patches, and logs were inventoried in [`research/results/forensic/artifact_inventory.csv`](file:///d:/CodeBase/AgentCore-Main/research/results/forensic/artifact_inventory.csv).
- Hashes of all task definitions and manifest files were recorded.

---

## 2. Reconstructed Sample Counts

| Experiment | Protocol Planned | Physically Executed | Status / Discrepancy |
| :--- | :---: | :---: | :--- |
| **Exp 1: Extension Locality** | 360 cells (12 x 6 x 5) | 37 runs | **Truncated**: Target tasks executed at n=1 (with T01 at n=2). MS Agent & DeepSeek omitted. |
| **Exp 2: Requirement Evolution** | 30 sequences (3 x 10) | 3 suites (10 phases each) | **Valid**: 10/10 tests executed and passing across AgentCore, LangGraph, PydanticAI. |
| **Exp 3: Abstraction Count** | 6 frameworks | 6 frameworks | **Complete**: AST extraction executed across all 6 frameworks in `structural_metrics.json`. |
| **Exp 5: Defect Locality** | 15 scenarios (3 x 5) | 3 suites (5 faults each) | **Valid**: 5/5 tests executed and passing across AgentCore, LangGraph, PydanticAI. |

---

## 3. Independence & Duplicate Analysis
- Direct diff comparison across replicates was conducted for the two independent T01 pilot replicates (`pilot_agentcore_T01` vs `pilot_agentcore_T01_R2` and `pilot_langgraph_T01` vs `pilot_langgraph_T01_R2`).
- Jaccard similarity between R1 and R2 patches was **0.48** for AgentCore and **0.52** for LangGraph, confirming independent syntactic construction without mechanical copy-pasting.

---

## 4. Reconstructed Locality ($P_{\text{ext}}$)
- Every patch in `research/runs/` was inspected:
  - AgentCore implementations introduced new layer classes (`RetryLayer.cs`, `TelemetryLayer.cs`, etc.) with zero edits to core runtime files ($P_{\text{ext}} = 0$).
  - LangGraph implementations modified `agent_graph.py` to add nodes, conditional edges, and compile parameters ($P_{\text{ext}} = 1$).
  - PydanticAI modified `agent.py` or runner scripts ($P_{\text{ext}} = 1$).
  - OpenAI Agents modified `agent_app.py` ($P_{\text{ext}} = 1$).
- Reconstructed locality matched reported manifests in **37 out of 37 runs (100.0% match)**.

---

## 5. Experimental Fairness & Alternative Interpretations

### Critical Alternative Explanations
1. **Architectural vs Script-Level Separation**:
   - In AgentCore, the architecture enforces that cross-cutting concerns reside in endomorphic layers.
   - In LangGraph, developers typically define the graph structure in application code. Treating `agent_graph.py` as part of the "core runner" penalizes graph architectures by design.
2. **Defect Blast Radius**:
   - In AgentCore, faults in decorator layers are isolated by the compiler interface boundary.
   - In LangGraph, shared graph state channels allow untyped or loosely typed payloads to propagate between nodes.

---

## 6. Required Actions for Paper Submission
1. **Accurate Sample Scoping**: Explicitly state in the paper that Experiment 1 evaluated 37 executed task runs across 4 frameworks (with full 12-task coverage for AgentCore and LangGraph, and target cross-cutting coverage for PydanticAI and OpenAI), rather than claiming a completed 360-run matrix.
2. **Acknowledge Boundary Paradigm Bias**: Clearly discuss the validity threat that graph architectures couple workflow definition with application code, which naturally increases file-level modification metrics under strict boundary definitions.
3. **Report Discrete Bootstrap Limitations**: Acknowledge that the zero-width confidence interval ($[1.000, 1.000]$) is a mathematical consequence of discrete invariant outcomes across the tested tasks.

---
*Forensic audit compiled independently from raw repository state.*
