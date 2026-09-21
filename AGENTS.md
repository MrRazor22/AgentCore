# STRICT OPERATIONAL RULES

## 1. NO CODE MODIFICATIONS WITHOUT EXPLICIT APPROVAL
- You are STRICTLY FORBIDDEN from calling `write_to_file`, `replace_file_content`, or making source changes until the user explicitly says "yes", "proceed", "do it", or "apply".
- State clearly and concisely in chat what changes will be made instead of creating explicit plan.md files (unless explicitly asked) or outputting verbose token-wasting diffs, and STOP your turn to wait for user confirmation.
- Never make speculative, panic, or knee-jerk code edits.

## 2. NATURAL & TO-THE-POINT CONVERSATION
- Talk like a human engineer replying directly to a friend/colleague: direct, clear, and to the point.
- Provide the full, essential answer without essay-length filler, verbose intros/outros, or token-wasting lectures.
- Only provide deep dives or long explanations when explicitly asked.

## 3. INTERFACE-FIRST ARCHITECTURE FOR BEHAVIORS
- Always define an interface for any class or object that exhibits behavior.
- Always depend on and consume interfaces rather than concrete classes.
- Keep interfaces small, focused, and razor-sharp.

## 4. CALM, DISCIPLINED EXECUTION
- Verify all assumptions first.
- Present solutions concisely.
- Never patch over architecture without deliberate reasoning and user alignment.

## 5. GENERIC & MULTIMODAL BY DESIGN
- Always consider multimodal and future extensibility generically. Never design with a narrow, myopic text-only view.
- Keep root contracts, pipeline layers, and data models generic across all modalities (media/binary, structured data, text).
- Never attach single-modality operations (e.g., character slicing, flat text conversion) to root contracts.
- Use isolated, optional capability contracts for modality-specific behaviors rather than polluting root interfaces.

## 6. RULE UPDATES TARGET GLOBAL USER RULES
- When the user asks to update rules or add guidelines, always target and update the global user rules file (`~/.gemini/config/AGENTS.md`) in addition to repository-level rules.

## 7. CONCISE CODE & ZERO DUPLICATION
- Correct code is naturally lean. Never write repetitive flows or verbose boilerplate.
- Leverage modern language constructs (e.g. primary constructors, concise guard clauses) to minimize lines while preserving clarity and zero-allocation performance.
- Zero bloat, zero speculative layers, zero redundant checks.
- AGGRESSIVELY PRUNE DEAD CODE & REDUNDANT OVERLOADS: Remove dead, obsolete, or duplicate methods, wrappers, and overloads when making new changes.
- DISTINGUISH PUBLIC EXTENSION UTILITIES FROM DEAD CODE: Never delete intentional public library API extension methods (such as pipeline inspection utilities like `FindLayer`) simply because they lack internal callers within the library repository itself. Dead code refers to superseded internal logic and redundant wrapper overloads, NOT intentional public framework APIs.

## 8. DESIGN FIRST — NO BUILDS OR TESTS WITHOUT EXPLICIT COMMAND
- Focus on sound architecture and minimal design first.
- DO NOT run tests or build commands (`dotnet build`, `msbuild`, etc.) automatically or prematurely.
- Only run builds or tests when the user explicitly commands: "build", "run tests", or "run and check tests".

## 9. STRICT GROUNDING & MANDATORY EXHAUSTIVE VERIFICATION
- ABSOLUTE PROHIBITION ON SPECULATION: Never state assumptions, guesses, or extrapolations as facts. If code was not directly inspected in the turn, you must not make claims about how it works.
- MANDATORY EXHAUSTIVE MULTI-SOURCE INSPECTION: When asked to analyze, compare, or answer across multiple repositories, agents, or files, you MUST explicitly search, open, and verify the relevant files in EVERY SINGLE requested repository/source before formulating an answer. Never inspect 1 or 2 files and generalize to the rest.
- CITE DIRECT CODE EVIDENCE: Every architectural claim about third-party frameworks must cite the exact file path and symbol/line range verified.
- IMMEDIATE ADMISSION OF UNVERIFIED FACTS: If a repository or file cannot be checked or is unavailable, explicitly state that it was not checked rather than guessing its behavior.

## 10. NO COMPLETED LISTS WITHOUT DIRECT FILE INSPECTION PROOF
- It is STRICTLY FORBIDDEN to output an itemized list or summary of multiple repositories, frameworks, or files if even ONE item was not directly viewed (`view_file`) or searched in the active turn.
- NEVER fabricate, fill in, or guess details for uninspected items just to complete a list or count.
- If inspection of any item is incomplete or interrupted, you must explicitly state: "The following items were NOT inspected yet: [list]", and only discuss what was directly verified with exact line links.

## 11. NO TOKEN-WASTING MECHANICAL RENAMES — ASK USER
- Never waste tokens performing mechanical symbol, type, or file renames across multiple files or updating cascading call sites.
- Always ask the user to perform widespread renaming changes directly in their IDE—even mid-task or mid-refactor—as it is trivial for the user and saves tokens and context.

## 12. STREAMING CONTEXT & WRITE-AHEAD DURABILITY
- Context is the single centralized assembly point for real-time streaming events from both LLM and tooling.
- Streams must be ingested chunk-by-chunk into Context as they arrive to ensure Write-Ahead Log (WAL) durability and crash resilience; waiting for stream completion risks catastrophic data loss mid-turn.
- Context owns assembling in-flight event streams into complete semantic message history; callers must never bypass Context to maintain parallel streaming state.

## 13. ZERO SPECULATIVE VISIBILITY & NO INTERNAL PATCH SMELLS
- Never make types, methods, or properties `public` unless there is an explicit, verified need and the design is architecturally sound. Zero speculative APIs.
- `internal` accessibility is usually a code smell used to hide quick patches or leak implementation details between classes/assemblies. Design with clean ownership and explicit boundaries instead of exposing internals.

## 14. LEAN & ZERO-CONVENIENCE-BLOAT PHILOSOPHY
- Correct, well-architected code is naturally minimal and concise.
- Never write convenience wrappers, lazy helper overloads, or over-engineered boilerplate just to avoid refactoring existing code.
- ZERO CONVENIENCE ALIASES OR PARALLEL FLUENT SINKHOLES: Never create or keep duplicate convenience aliases (e.g. `With*` mirroring `Use*`, `Add*`, or `Remove*`). There must be exactly ONE canonical way to perform an operation.
- NEVER RETAIN PARALLEL CONVENIENCE APIS: Never retain obsolete, duplicate, or secondary fluent aliases for "caller convenience" or "backwards compatibility"—aggressively delete them immediately without waiting to be prompted.
- Minimal does NOT mean code golf; it means razor-sharp design where every line earns its existence.
- When new requirements emerge, do NOT lazily tack on convenience bloat—refactor and adapt existing abstractions so everything fits cleanly and cohesively.

## 15. SELF-DOCUMENTING CODE & NO EXPLANATORY COMMENTS
- If code requires comments to explain what it does, it is a design smell.
- Code must be self-documenting and readable through clear naming and clean structure.
- Do not write comments explaining logic or intent; make the code itself immediately obvious.

## 16. STRICT SIZE & COMPLEXITY LIMITS (SMELL DETECTORS)
- Max 150 lines per file: If a file exceeds 150 lines, it is doing too much and has a design smell.
- Max 4–5 methods per class: If a class has more than 4–5 methods, it violates Single Responsibility and must be decomposed into focused units.

## 17. ZERO HARDCODING & CONFIGURABLE BY DESIGN
- Never hardcode environment paths, URLs, timeouts, magic numbers, or operational toggles in application logic.
- Always prefer clean configuration injection so behaviors and environments are easily configurable.

## 18. FIRST-PRINCIPLES OOP MODELING & CONTINUOUS SIMPLIFICATION
- Continuously challenge designs: "Can we do this better and more minimally?"
- Identify the primitive, fundamental concept and model it using real-world object-oriented metaphors.
- Readability is the primary goal; minimal code is the natural side-effect of accurate, real-world modeling.
- In ambiguous situations, anchor and clarify the design with concrete real-world metaphors and examples.

## 19. MINIMAL CODE IS THE ONLY CORRECT CODE — PRIMITIVES FIRST, ZERO TOP-DOWN BLOAT
- Less code is correct code. Correct code WILL BE minimal.
- Always design strictly bottom-up from the raw primitive. Only add abstractions when strictly proven and needed—never top-down speculative abstractions.
- Never introduce bloat, speculative wrapper layers, helper classes, custom enums for things frameworks already provide, or procedural UI boilerplate.
- Keep sample apps and consumer code dead simple, declarative, and minimal.

## 20. PROJECT LIFECYCLE & COMPLETION STATUS
- Only `AgentCore`, `AgentCore.MCP`, `AgentCore.Layers`, and `AgentCore.LLM.Tornado` are completed, production-ready projects.
- All other projects (including `AgentCore.MultiAgent`, etc.) are actively under development and must not be considered complete.

