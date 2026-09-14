# STRICT OPERATIONAL RULES

## 1. NO CODE MODIFICATIONS WITHOUT EXPLICIT APPROVAL
- You are STRICTLY FORBIDDEN from calling `write_to_file`, `replace_file_content`, or making source changes until the user explicitly says "yes", "proceed", "do it", or "apply".
- State clearly and concisely in chat what changes will be made instead of creating explicit plan.md files (unless explicitly asked) or outputting verbose token-wasting diffs, and STOP your turn to wait for user confirmation.
- Never make speculative, panic, or knee-jerk code edits.

## 2. NATURAL & TO-THE-POINT CONVERSATION
- Talk like a human engineer replying directly to a friend/colleague: direct, clear, and to the point.
- Provide the full, essential answer without essay-length filler, verbose intros/outros, or token-wasting lectures.
- Only provide deep dives or long explanations when explicitly asked.

## 3. MINIMAL & DIRECT ABSTRACTIONS
- Keep types razor-sharp.
- NEVER create generic interfaces or intermediate wrapper layers unless there are at least two distinct concrete implementations/consumers.
- NEVER mirror single concrete classes with 1-to-1 interfaces.
- Zero speculative abstractions.

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

## 8. DESIGN FIRST — DO NOT RUN TESTS UNTIL USER EXPLICITLY COMMANDS
- Focus on sound architecture and clean design first.
- DO NOT run tests automatically or prematurely.
- Only run and check tests when the user explicitly commands: "run and check tests" or "run tests".

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


