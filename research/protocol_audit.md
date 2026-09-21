# Protocol and Repository Audit

**Author**: Lead Empirical Research Execution Agent  
**Date**: September 21, 2026  
**Document**: `research/protocol_audit.md`  
**Status**: Frozen Protocol Audit Complete  

---

## 1. Executive Identity Verification

| Framework Target | Source Repository Path | Protocol Anchor Commit SHA | Verified Local Commit SHA | Match Status | Runtime / Language |
|---|---|---|---|---|---|
| **AgentCore** (Treatment) | `d:\CodeBase\AgentCore-Main` | Immutable checkout (`HEAD`) | `b11d0e489f08e1cc73284a1088e56d3720386597` | **Exact Match** | .NET 10.0.400 / 8.0 (C#) |
| **LangGraph** | `D:\CodeBase\Popular Agent Frameworks\langgraph` | `644815f9e5bc52ad8f7a5227a456227e9c3e639b` | `644815f9e5bc52ad8f7a5227a456227e9c3e639b` | **Exact Match** | Python 3.14.2 |
| **PydanticAI** | `D:\CodeBase\Popular Agent Frameworks\pydantic-ai` | `c4898abb54dc25ae6f6aef208a4c0661b30a455e` | `c4898abb54dc25ae6f6aef208a4c0661b30a455e` | **Exact Match** | Python 3.14.2 |
| **OpenAI Agents SDK Python** | `D:\CodeBase\Popular Agent Frameworks\openai-agents-python` | `fdf21db62c303a3db54b0dfbee82de2141fa2799` | `fdf21db62c303a3db54b0dfbee82de2141fa2799` | **Exact Match** | Python 3.14.2 |
| **Microsoft Agent Framework** | `D:\CodeBase\Popular Agent Frameworks\microsoft-agent-framework` | `703fbce285ee0f026e5effcadfb9e65aab7f5d84` | `703fbce285ee0f026e5effcadfb9e65aab7f5d84` | **Exact Match** | Python 3.14.2 |
| **DeepSeek Harness** | `D:\CodeBase\Popular AI Agnets\deepseek-harness` | `ddefc45fbc7f8e46dd73185e68295696d1297887` | `ddefc45fbc7f8e46dd73185e68295696d1297887` | **Exact Match** | Python 3.14.2 |

All external baseline repositories were fetched and checked out to their exact immutable protocol anchor commits. Zero commit substitutions or silent upgrades.

---

## 2. AgentCore Source Identity
- **Working Tree**: `d:\CodeBase\AgentCore-Main`
- **Head Commit**: `b11d0e489f08e1cc73284a1088e56d3720386597`
- **Status**: Clean baseline; treated as immutable experimental subject. No modifications to `AgentCore/*.cs` will be made during benchmark runs. All harness adapters and tests reside exclusively in `research/`.

---

## 3. Toolchain & Environment Audit
- **Host OS**: Windows 11 (build 22631)
- **Git**: 2.47.1.windows.1
- **.NET SDK**: 10.0.400
- **Python Runtime**: Python 3.14.2 64-bit
- **Pre-installed Scientific Libraries**: `numpy` 2.4.2, `scipy` 1.17.0, `matplotlib` 3.10.8, `networkx` 3.6.1, `rich` 14.3.2

---

## 4. Frozen Protocol Requirements & Verification Checklist
1. **Capability Universe**: G0 through G6, G9-G10 primary; G7 (multi-agent) separate stress test; G8 (human interruption) run-control only.
2. **Tasks**: 12 primary tasks (T01-T12).
3. **Primary Estimand**: Contrast in external propagation $P_{ext} = |M(r) - B(r)|$ with task as generalization unit.
4. **Stopping Rules**: Max 90 minutes wall-clock or 20 repair iterations.
5. **Statistical Model**: Mixed-effects model, Holm multiplicity correction, task-cluster bootstrap.
6. **Blinded Ratings**: Two independent raters evaluating R1-R4 with Cohen's kappa.
