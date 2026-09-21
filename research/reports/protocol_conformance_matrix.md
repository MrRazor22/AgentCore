# Protocol Conformance Matrix

**Author**: Lead Empirical Research Execution Agent  
**Date**: September 21, 2026  
**Document**: `research/protocol_conformance_matrix.md`  
**Status**: Authoritative Pre-Execution Audit  

---

## 1. Conformance Matrix

| Protocol Requirement | Specified in Manuscript? | Implemented in Harness? | Executable on Environment? | Deviation Detected? | Justification / Adaptation Details |
|---|---|---|---|---|---|
| **R01: Immutable Subject Commit** | Yes (AgentCore commit frozen) | Yes | Yes | None | Subject locked to `b11d0e489f08e1cc73284a1088e56d3720386597`. |
| **R02: Baseline Repository Commits** | Yes (5 external pinned SHAs) | Yes | Yes | None | All 5 pinned commits fetched and validated via Git. |
| **R03: 12 Primary Tasks (T01–T12)** | Yes (Sec 7.5) | Yes | Yes | None | T01-T12 specs, fixtures, acceptance criteria fully defined. |
| **R04: Independent Implementation Units** | Yes (Sec 7.4, 5 units per cell) | Yes | Yes | None | Isolated worktree/sandbox execution with distinct seeds. |
| **R05: Stopping Rules (90m / 20 iters)** | Yes (Sec 7.8) | Yes | Yes | None | Built into benchmark execution loop. |
| **R06: External Propagation $P_{ext}$ Metric** | Yes (Sec 8) | Yes | Yes | None | Calculated as $\|M(r) - B(r)\|$ with normalized $I_{ext}(r)$. |
| **R07: Core Kernel Touch Flag $C_{core}$** | Yes (Sec 8) | Yes | Yes | None | Binary flag checked against `Agent.cs` / engine AST. |
| **R08: Public API / Dependency Edge Tracking** | Yes (Sec 8) | Yes | Yes | None | Extracted via language AST / Roslyn / Python inspect. |
| **R09: Primitive Ablation (L, T, C)** | Yes (Sec 6) | Yes | Yes | None | Evaluated against R1-R4 operational rubric. |
| **R10: Blinded Dual Raters (Cohen's Kappa)** | Yes (Sec 6.4) | Yes | Yes | None | Automated rater harnesses blinded to framework ID. |
| **R11: Requirement Evolution (10 Phases)** | Yes (Sec 7.5, Exp 2) | Yes | Yes | None | Sequential commit history across 5 sequences. Phase 10 separated. |
| **R12: Fault Injection (5 Defects)** | Yes (Exp 5) | Yes | Yes | None | Systematic injection of FLT-1 through FLT-5. |
| **R13: Structural Metrics (Types, Methods, DIT, CBO)** | Yes (Exp 3) | Yes | Yes | None | Scripted AST extraction across all 6 codebases. |
| **R14: Confirmatory Statistical Plan** | Yes (Sec 7.9) | Yes | Yes | None | Mixed-effects regression, Holm correction, cluster bootstrap. |
| **R15: Reproducibility Manifest Schema** | Yes (Appendix C) | Yes | Yes | None | Per-run JSON manifests with hashes and timestamps. |

Zero unrecorded deviations. The execution protocol conforms strictly to the frozen manuscript specifications.
