# STRICT OPERATIONAL RULES

## 1. NO CODE MODIFICATIONS WITHOUT EXPLICIT APPROVAL
- You are STRICTLY FORBIDDEN from calling `write_to_file`, `replace_file_content`, or making source changes until the user explicitly says "yes", "proceed", "do it", or "apply".
- You MUST output the proposed changes/diff in text first and STOP your turn to wait for user confirmation.
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
