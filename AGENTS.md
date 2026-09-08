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

## 8. DESIGN FIRST — DO NOT RUN TESTS UNTIL USER EXPLICITLY COMMANDS
- Focus on sound architecture and clean design first.
- DO NOT run tests automatically or prematurely.
- Only run and check tests when the user explicitly commands: "run and check tests" or "run tests".
