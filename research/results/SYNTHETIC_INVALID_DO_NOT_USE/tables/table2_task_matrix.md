| Task ID | Task Name | Category | Natural Boundary B(r) Artifacts | Outside Boundary (Incurs P_ext) | Expressiveness Seam |
|---|---|---|---|---|---|
| **T01** | Retry with Exponential Backoff | Cross-cutting | LLM decorator, retry policy/tests | Core runner, tool registry, context | LLM decorator / client wrapper |
| **T02** | Structured Telemetry & Logging | Cross-cutting | Telemetry layer, log handler | Core runner, tool logic, context | Layer decorator / handler pipeline |
| **T03** | Tool Execution Approval (HITL) | Cross-cutting | Toolbox decorator, approval delegate | Core runner, LLM client, context store | Toolbox decorator / tool middleware |
| **T04** | Semantic / Response Caching | Cross-cutting | Caching layer, cache store/tests | Core runner, tool registry, context | LLM decorator / client cache |
| **T05** | Rate Limiting (Token Bucket) | Cross-cutting | Rate limit layer, bucket policy | Core runner, tool registry, context | LLM decorator / client limiter |
| **T06** | Input Validation & Guardrails | Cross-cutting | Guardrail layer, validator policy | Core runner, tool registry, context | Layer decorator / validator filter |
| **T07** | Durable Context & WAL Recovery | State-execution| Context layer, WAL store/tests | Core runner, LLM client, tool registry | Context decorator / checkpointer |
| **T08** | Context Compaction / Summary | State-execution| Compactor layer, summarizer | Core runner, LLM client, tool registry | Context decorator / memory trimmer |
| **T09** | Model Provider Substitution | State-execution| ILLM provider adapter, tests | Core runner, tool registry, context | ILLM implementation / ModelClient |
| **T10** | Real-time Stream Observation | State-execution| Stream tap / observer, tests | Core runner, tool registry, context | Stream layer / event generator |
| **T11** | Controlled Interruption | State-execution| Interruption state, run controller | Tool registry, LLM client | Graph interrupt / run controller |
| **T12** | Dynamic Tool Discovery | State-execution| Toolbox layer filter, tests | Core runner, LLM client, context | Toolbox decorator / dynamic registry |
