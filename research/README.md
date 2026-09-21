# Empirical Research & Replication Suite

This directory contains the complete empirical evaluation for the research manuscript *"Primitive-First Agent Architecture"*.

## Structure

- **`EMPIRICAL_RESULTS_REPORT.md`**: The definitive primary empirical evaluation report synthesizing all executed benchmark findings across 4 evaluated frameworks.
- **`FINAL_FORENSIC_AUDIT.md`**: Rigorous adversarial forensic audit examining sample scoping, independence, natural boundary definitions, and validity threats.
- **`protocol_conformance_matrix.md`**: Requirement-by-requirement audit matrix comparing the frozen protocol to actual physical execution.
- **`tasks/`**: Preregistered, frozen benchmark task definitions ($T01\text{--}T12$) and natural boundary ($B(r)$) specifications.
- **`runs/`**: Concrete physical run directories containing the implemented source code, unit test suites, git patches (`run.patch`), and execution manifests (`manifest.json`):
  - `agentcore_T01` to `agentcore_T12`: AgentCore locality implementations ($P_{\text{ext}} = 0.000$).
  - `langgraph_T01` to `langgraph_T12`: LangGraph locality implementations ($P_{\text{ext}} = 1.000$).
  - `pydanticai_T01` to `pydanticai_T07`: PydanticAI cross-cutting implementations ($P_{\text{ext}} = 1.000$).
  - `openai_agents_T01` to `openai_agents_T07`: OpenAI Agents SDK implementations ($P_{\text{ext}} = 1.000$).
  - `evolution/`: 10-phase requirement evolution test suites (AgentCore, LangGraph, PydanticAI).
  - `fault_injection/`: 5-defect fault injection test suites (AgentCore, LangGraph, PydanticAI).
  - `structural_metrics.json`: Direct AST extraction results across all 6 architectures.
- **`results/`**:
  - `figures/`: High-resolution publication figures (`fig1_locality_comparison.png`, `fig2_evolution_invasiveness.png`, `fig3_conceptual_load_index.png`, `fig4_defect_leakage.png`).
  - `forensic/`: Raw audit CSVs (`artifact_inventory.csv`, `reconstructed_locality.csv`, `duplicate_analysis.csv`, `fairness_matrix.csv`).
  - `SYNTHETIC_INVALID_DO_NOT_USE/`: Quarantined legacy prototype scripts and synthetic profiles kept solely for historical audit traceability.
- **`benchmarks/`**: Reproducible analysis scripts:
  - `statistical_bootstrapper.py`: Non-parametric cluster bootstrap (10,000 resamples) and figure generation.
  - `structural_analyzer.py`: Automated AST parsing across framework repositories.
  - `forensic_auditor.py`: First-principles verification and reconciliation script.
