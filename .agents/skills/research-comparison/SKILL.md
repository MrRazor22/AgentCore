---
name: research-comparison
description: >-
  Systematically conduct rigorous, adversarial, publication-grade research comparisons
  between AgentCore and third-party frameworks/paradigms (e.g. DeepSeek Cordis).
  Extracts the real production problem statement, formal mathematics, reproduces or measures
  promised future-work benchmarks, and produces crisp, no-filler comparison treatises.
---

# Research Comparison Skill

This skill governs the systematic comparison of AgentCore's **Primitive-First Architecture** against state-of-the-art literature and frameworks.

## 4-Pillar Comparison Protocol

Whenever conducting a research comparison against a paper or framework:

### Pillar 1: The Real Production Problem Statement
- Dig past the abstract and theoretical framing to identify the **actual real-world production incident, platform, or failure** that motivated the authors (e.g., Koishi multi-platform chat bot on Node.js requiring restarts when unloading plugins).
- Quote verbatim problem definitions from the paper (§1.1, §1.2, case studies).

### Pillar 2: Core Mathematics & Theorem Confrontation
- Contrast state representations, algebra, and invariants:
  - **Shared mutable state** vs. **Disjoint orthogonal basis** $\mathcal{A} = (\mathcal{L}, \mathcal{T}, \mathcal{C})$.
  - **Dynamic runtime undo / recovery engines** ($\tau^{-1}$ accumulator stacks) vs. **Static endomorphism monoid unwrapping** $(\mathrm{End}(F), \circ, \mathrm{id}_F)$.
  - **Dynamic dependency graph rewriting** vs. **Static constructor injection**.
- Confront the formal proofs: Identify where the opposing math breaks down in the real world (e.g., irreversible I/O effects breaking $\tau^{-1}$ inverses).

### Pillar 3: Empirical Benchmarking & "Future Work" Execution
- Check the opponent's paper for admissions of missing measurements ("Threats to Validity", "Future Work").
- Execute those exact unperformed benchmarks against AgentCore:
  1. **Cognitive Load Index (CLI)** via AST parsing (Types, Methods, Complexity).
  2. **Extension Locality ($L_{\mathrm{mod}}$)**: Blast radius to core when adding cross-cutting concerns.
  3. **Defect Quarantine ($L_{\mathrm{leak}}$)**: Blast radius when an extension faults.
- Report physical, audited measurements only—never fabricate data.

### Pillar 4: Benefits, Advantages & Superiority Matrix
- Translate mathematical theorems into concrete engineering trade-offs:
  - Runtime overhead and memory footprint.
  - Developer cognitive load.
  - Compile-time safety vs. runtime crashes.
- Produce a crisp, zero-filler comparison document with no hand-waving.
