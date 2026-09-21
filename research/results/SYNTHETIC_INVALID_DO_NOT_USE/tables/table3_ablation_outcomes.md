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
