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

## Architectural Decisions

### Delegates Over Base Classes

We use `Func<>` delegates for hooks and extensibility instead of abstract base classes or interfaces. This eliminates:
- Empty override methods
- Generic type parameters
- Registration ceremony
- Inheritance chains

Users assign only what they need. No empty overrides, no base class requirements.

### Fire-and-Forget Observation

Hooks are observation points, not transformation points. They don't modify messages or tool results. A pipeline implies ordering, middleware chains, next-delegates—all overhead with no benefit for fire-and-forget observation.

### Data Over Behavior

Skills, scratchpads, and memory items are data structures, not behavioral objects. They're registered, configured, and passed around—not instantiated with lifecycle methods. This keeps the system simple and composable.

### Post-Reactive Compaction

We compact context after LLM calls, not before. Pre-counting tokens is approximate and adds complexity. Using actual token counts from responses is exact and requires no additional infrastructure.

### Explicit Over Implicit

The agent explicitly loads skills via tool calls. Skills are not silently injected into context. This makes the agent's reasoning traceable and the system predictable.

## Contribution Guidelines

When adding features to AgentCore:

1. **Study existing patterns**—don't introduce new abstractions unless necessary
2. **Prefer delegates** over interfaces and base classes for extensibility
3. **Keep data structures simple**—records and immutable types
4. **Avoid ceremony**—no registration, no configuration objects unless needed
5. **Measure against competitors**—can this be expressed in fewer lines without losing capability?
6. **Test the philosophy**—does this change make the code clearer or just shorter?

## Examples

### Hooks: 30 Lines vs 176 Lines

OpenAI Agents Python uses abstract base classes with generics (~176 lines). AgentCore uses a single record with nullable Func delegates (~30 lines). Same capability, zero ceremony.

### Skills: Data Over Classes

Skills are `record Skill(string Name, string Description, string Content)`. No interface, no lifecycle, no base class. Just data that the agent can load.

### Compaction: Single Path

OpenCode has pre-reactive and post-reactive compaction. AgentCore has one post-reactive path that handles both successful calls and context overflow exceptions. Simpler logic, same result.
