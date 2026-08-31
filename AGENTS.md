# STRICT OPERATIONAL RULES

## 1. NO CODE MODIFICATIONS WITHOUT EXPLICIT APPROVAL
- You are STRICTLY FORBIDDEN from calling `write_to_file`, `replace_file_content`, or making source changes until the user explicitly says "yes", "proceed", "do it", or "apply".
- State clearly and concisely what changes will be made instead of outputting verbose, token-wasting diffs, and STOP your turn to wait for user confirmation.
- Never make speculative, panic, or knee-jerk code edits.

## 2. MINIMAL & DIRECT ABSTRACTIONS
- Keep types razor-sharp.
- NEVER create generic interfaces or intermediate wrapper layers unless there are at least two distinct concrete implementations/consumers.
- NEVER mirror single concrete classes with 1-to-1 interfaces.
- Zero speculative abstractions.

## 3. CALM, DISCIPLINED EXECUTION
- Verify all assumptions first.
- Present solutions concisely.
- Never patch over architecture without deliberate reasoning and user alignment.

## 4. GENERIC & MULTIMODAL BY DESIGN
- Always consider multimodal and future extensibility generically. Never design with a narrow, myopic text-only view.
- Keep root contracts, pipeline layers, and data models generic across all modalities (media/binary, structured data, text).
- Never attach single-modality operations (e.g., character slicing, flat text conversion) to root contracts.
- Use isolated, optional capability contracts for modality-specific behaviors rather than polluting root interfaces.

## 5. RULE UPDATES TARGET GLOBAL USER RULES
- When the user asks to update rules or add guidelines, always target and update the global user rules file (`~/.gemini/config/AGENTS.md`) in addition to repository-level rules.
