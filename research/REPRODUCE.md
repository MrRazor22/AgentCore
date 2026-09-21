# Reproducing the Empirical Benchmark

This guide outlines the step-by-step instructions to reproduce all empirical benchmark results, tables, metrics, and figures from source.

## Prerequisites

1. **.NET 8.0 / 9.0 SDK**: Required to build and run AgentCore and its test suites.
2. **Python 3.10+**: Required to run the benchmark harness and Python-based baseline tests.
3. **Pip Dependencies**:
   ```bash
   pip install -r research/requirements.txt
   ```

---

## One-Command Full Reproduction

To run the complete benchmark suite, verify all manifests, run statistical bootstrapping, and generate all publication figures:

```bash
python research/run_benchmark.py
```

---

## Selective Experiment Execution

You can run individual experiments using the `--experiment` flag:

1. **Experiment 1 (Extension Locality $P_{\text{ext}}$)**:
   ```bash
   python research/run_benchmark.py --experiment locality
   ```
2. **Experiment 2 (Requirement Evolution across 10 Phases)**:
   ```bash
   python research/run_benchmark.py --experiment evolution
   ```
3. **Experiment 3 (AST Abstraction & Conceptual Load Index)**:
   ```bash
   python research/run_benchmark.py --experiment structural
   ```
4. **Experiment 5 (Defect Locality under 5 Injected Faults)**:
   ```bash
   python research/run_benchmark.py --experiment faults
   ```
5. **Statistical Bootstrap & Figure Generation (10,000 Resamples)**:
   ```bash
   python research/run_benchmark.py --experiment bootstrap
   ```

---

## Direct Test Suite Execution

You can also directly execute the unit test suites of the individual frameworks:

### AgentCore
```bash
# Evolution test suite (10/10 tests)
dotnet test research/runs/evolution/agentcore/EvolutionAgentCore.csproj

# Fault injection test suite (5/5 tests)
dotnet test research/runs/fault_injection/agentcore/FaultAgentCore.csproj
```

### LangGraph
```bash
# Evolution test suite (10/10 tests)
python -m unittest research/runs/evolution/langgraph/test_langgraph_evolution.py

# Fault injection test suite (5/5 tests)
python -m unittest research/runs/fault_injection/langgraph/test_langgraph_faults.py
```

### PydanticAI
```bash
# Evolution test suite (10/10 tests)
python -m unittest research/runs/evolution/pydantic_ai/test_pydantic_ai_evolution.py

# Fault injection test suite (5/5 tests)
python -m unittest research/runs/fault_injection/pydantic_ai/test_pydantic_ai_faults.py
```

---

## Output Artifacts

- **Figures**: Generated into `research/results/figures/`
- **Forensic Audit CSVs**: Generated into `research/results/forensic/`
- **Summary Report**: Detailed analysis available in `research/EMPIRICAL_RESULTS_REPORT.md`
