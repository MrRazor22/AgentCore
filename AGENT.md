# AgentCore Design Philosophy

## Core Principle

Less code is a side effect of correct logic. Not a goal unto itself—bloat comes from wrong abstractions.

## Guidelines

### No Needless Convenience

If the concept is clear, the code is short. Stupid logic needs convenience wrappers. We don't add helper methods just to save a few keystrokes—we add them when they express a clear, reusable concept.

### Match or Exceed the Competition

Study what Codex, OpenCode, Claude Code do—then express it in 1/10th the code through sharper design. We aim for feature parity with major frameworks, not line count parity.

### No Design Smell

Reducing code that introduces smell is worse than the bloat it replaced. If a simplification makes the code harder to understand or maintain, it's not a simplification—it's a regression.

### Use Intended Abstractions

If the framework provides an abstraction specifically for building valid message collections (such as `AddIfValid`), but the framework's own default workflow doesn't use it, then either the abstraction is wrong or the workflow is bypassing the intended design. Framework abstractions must be used consistently across all default implementations.

## Architectural Decisions

### Data Over Behavior

Skills, scratchpads, and memory items are data structures, not behavioral objects. They're registered, configured, and passed around—not instantiated with lifecycle methods. This keeps the system simple and composable.

### Post-Reactive Compaction

We compact context after LLM calls, not before. Pre-counting tokens is approximate and adds complexity. Using actual token counts from responses is exact and requires no additional infrastructure.

### Explicit Over Implicit

The agent explicitly loads skills via tool calls. Skills are not silently injected into context. This makes the agent's reasoning traceable and the system predictable.

## Logging Philosophy

### 1. Component Independence

Determine the logging strategy based on each component's distinct responsibilities (`ChatContext`, `Tooling`, `ReActWorkflow`, `Agent.Builder`, `MEAILLM`). Do not force a rigid, blanket logging template (e.g. mandatory start/completion pairs) across every component.

### 2. Cross-Component Deduplication Test

Whenever considering a log, ask: *"If I remove this log, can I still infer this information from another component's logs?"*
* If **yes**, it is redundant and should be omitted.
* If **no** (e.g., workflow entering a batch tool-execution phase vs. individual tool execution duration), and it carries distinct operational/state value, keep it.

### 3. Production Debugging Mindset

Optimize for the experience of reading logs during a real production debugging session, not for satisfying a fixed logging pattern. Every log must answer at least one useful debugging question. Avoid purely narrative logs ("Starting...", "Finished...", "Method called").

### 4. Structured State Over Prose

Prefer structured properties over descriptive prose. The message should describe the event cleanly, while structured fields carry diagnostic state (token counts, limits, durations, error details).

### 5. Avoid Unnecessary Churn

When reviewing existing logs, first determine whether they already provide meaningful diagnostic value. Do not rewrite logs solely for cosmetic consistency if they are already clear and useful.

### 6. Lean Log Levels

Default to using only `LogInformation`, `LogWarning`, and `LogError`. Avoid introducing `Debug` and `Trace` unless there is a genuinely compelling reason.

### 7. Implementation-Agnostic Messaging

Describe operations from the perspective of the system rather than internal method names. Prefer `Preparing prompt`, `Conversation updated`, `Executing tool` over `Commit()`, `ReplaceWithSummary()`, or `InvokeCore()`.

### 8. Unexpected Failures vs. Expected Absence

Distinguish between expected feature variations and true operational errors:
* **Log Fallbacks & Unexpected Errors**: Emit `LogWarning` when a requested feature configuration fails and falls back to a safer default (e.g. JSON Schema format falling back to standard JSON) or when unexpected reflection/parsing exceptions occur.
* **Do Not Log Expected Absence**: Do not log warnings when optional metadata (such as reasoning contents or incremental tool deltas) is simply not exposed by a provider.

## Contribution Guidelines

When adding features to AgentCore:

1. **Study existing patterns**—don't introduce new abstractions unless necessary
2. **Prefer simple patterns** over complex abstractions for extensibility
3. **Keep data structures simple**—records and immutable types
4. **Avoid ceremony**—no registration, no configuration objects unless needed
5. **Measure against competitors**—can this be expressed in fewer lines without losing capability?
6. **Test the philosophy**—does this change make the code clearer or just shorter?
7. **Apply component logging principles**—evaluate logging through the Cross-Component Deduplication Test, production debugging mindset, and expected vs. unexpected failure distinction.

## Examples

### Skills: Data Over Classes

Skills are `record Skill(string Name, string Description, string Content)`. No interface, no lifecycle, no base class. Just data that the agent can load.

### Compaction: Single Path

OpenCode has pre-reactive compaction and post-reactive compaction. AgentCore has one post-reactive path that handles both successful calls and context overflow exceptions. Simpler logic, same result.

### Logging: Component-Tailored Diagnostic Value

* `Agent.Builder`: Emits a single configuration summary log upon build completion (`Tools`, `Provider`, `Context`, `Workflow`, `LLMLayers`, `ToolingLayers`, `ContextLayers`) to capture total pipeline construction.
* `ChatContext`: Emits prompt preparation state (`Strategy`, `StagedMessages`, `StagedTokens`, `EstimatedTokens`, `Limit`) and compaction events to provide crucial context budgeting visibility.
* `Tooling`: Emits a single completion log capturing `ToolName` and `DurationMs` to measure tool performance without noisy start/result trace logs.
* `ReActWorkflow`: Logs workflow phase transitions (`Executing workflow iteration`, `Executing tools`) to distinguish workflow iteration steps from individual tool runs.
* `MEAILLM`: Emits `LogWarning` on feature configuration fallbacks (e.g., JSON Schema fallback) or unexpected raw payload parsing errors, while avoiding noise when optional metadata is absent.
