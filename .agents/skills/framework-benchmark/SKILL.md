---
name: framework-benchmark
description: >-
  Strict protocol for designing and running scientific, peer-reviewable benchmarks
  across AI agent frameworks (AgentCore, Microsoft Semantic Kernel, PydanticAI, LangGraph, etc.).
  Prohibits toy benchmarks, synthetic hacks, mismatched execution paths, and unverified assumptions.
---

# Scientific Agent Framework Benchmarking Protocol

Benchmarks are serious scientific artifacts. They are NOT marketing toys, toy projects, or synthetic hacks designed to flatter one framework over another.

## 1. Zero Toy Benchmarks & Zero Synthetic Hacks
- Never compare a full streaming pipeline against a trivial baseline (e.g. returning a hardcoded string).
- Never compare mismatched execution paradigms:
  - **Streaming vs Streaming only**: Never compare a chunk-by-chunk async streaming enumerator against a buffered single-shot method call.
  - **Async vs Async only**: Never compare synchronous loops against asynchronous pipelines.
  - **Identical Return Types**: Both sides must return identical data structures (e.g. counting stream events or tokens) so return-value allocations do not skew GC measurements.
- Never write synthetic hacks that bypass or handicap a competitor's real pipeline to fabricate artificial wins.

## 2. Mandatory Source-Grounding of Competitors
- **Never guess or extrapolate competitor APIs**: Before writing any benchmark adapter for a competitor (Python, .NET, or TS), you MUST open and directly inspect the competitor's official repository, public documentation, and official test suite.
- Use only the competitor's canonical, official public API. Do not use outdated, deprecated, or private methods.
- Verify how the competitor's own test suite tests streaming and tool calls (e.g. inspecting `tests/test_agent_runner_streamed.py` in OpenAI Agents or `pydantic_ai/models/test.py` in PydanticAI).

## 3. The 1:1 Identical Execution Sequence
Every framework under test must execute the exact same canonical real-world workload:
1. Ingest an identical user prompt (e.g. *"What is the weather in London?"*).
2. Serialize and provide identical tool definitions/schemas.
3. Emit a streaming tool call chunk from a deterministic mock LLM.
4. Execute the identical tool logic with identical arguments.
5. Ingest the tool result into context/history.
6. Emit streaming final text response chunks.
7. Consume the stream chunk-by-chunk to completion.

## 4. The 5 Valid Benchmark Dimensions
Instead of a single subjective "score", always report a multidimensional vector:
1. **Overhead Tax (Speed & Allocations)**:
   - Nanoseconds/microseconds per turn with zero-latency canned mock LLM.
   - Managed heap bytes allocated per turn.
   - Gen 0 / Gen 1 GC collection counts.
   - Normalized tax ratio ($\frac{T_{\text{framework}}}{T_{\text{baseline}}}$) to make cross-language comparisons scientifically honest.
2. **Durability & State Loss**:
   - Stream cutoff test: Drop connection at token 500. Measure `Tokens Saved / Tokens Sent`.
3. **Tool Dispatch Scaling**:
   - Measure latency and memory degradation as registered tools scale from 1 to 500.
4. **Long-Session Memory Footprint**:
   - Memory growth $M(n)$ over 100 and 1,000 continuous turns (detecting state leaks).
5. **Ergonomic Complexity (AST Metrics)**:
   - AST node count and cyclomatic complexity to implement identical tasks.

## 5. Lean, Self-Contained Implementation
- Keep benchmark code razor-lean and self-documenting.
- Avoid multi-tiered directory structures, intermediate JSON merger scripts, or daemon processes.
- Every test must be runnable with one standard command (`dotnet run -c Release` or `python bench_<framework>.py`) and completely reproducible by any outside engineer.
