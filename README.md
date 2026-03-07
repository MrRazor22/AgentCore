# AgentCore

An agent primitive for .NET — not a framework, a foundation.

An agent is a loop over a conversation. The industry wraps this in state graphs, session objects, memory abstractions, and middleware pipelines. AgentCore strips it down to what actually matters: a conversation, a loop, and tools. ~1,800 lines. 2 dependencies. Zero containers.

---

## Design Philosophy

### This is All You Need

Most agent frameworks solve problems they created. They introduce session state dictionaries, then need state management. They add memory abstractions, then need memory lifecycle hooks. They build middleware pipelines, then need filter chains to control them.

An LLM is stateless. You give it messages, it responds. AgentCore aligns with that reality instead of fighting it. The entire agent runtime is: load conversation → run the loop → save conversation. Everything else — context management, tool execution, crash recovery — falls out naturally from this design.

### No Wrapper Bloat

Many frameworks wrap LLM calls in request/response objects that add no value. AgentCore uses a simple record:

```csharp
public sealed record LLMCall(IReadOnlyList<Message> Messages, LLMOptions Options);
```

That's it. No builder patterns, no fluent configuration, no boilerplate. The record holds what it needs: messages and options.

### No Hardcoded Limits

AgentCore avoids magic numbers. Instead of hardcoding `MaxResultLength = 32768`, the `ContextManager` calculates available context dynamically:

- Takes model's context window from `LLMOptions.ContextLength`
- Reserves space for output (default: min(4096, 25% of context))
- Everything — messages, tool results, system prompts — is trimmed uniformly

This means tool results are trimmed the same way as conversation history: intelligently, based on actual available space.

### Agent as a Boundary

Semantic Kernel exposes `ChatMessageContent`, `FunctionResult` at the agent surface `KernelArguments`,. Google ADK exposes `session.state` and `session.events`. LangChain exposes `AIMessage` and `HumanMessage`. In every case, the agent leaks LLM internals to the caller because the "agent" is really just a thin wrapper around the model API.

AgentCore draws a hard boundary. **The agent is a service, not a wrapper.** At the surface:

- **Input:** a task, as a `string`
- **Output:** a result, as a `string`

Messages, roles, tool calls, context windows — these are internal implementation details. Your application code has zero coupling to LLM concepts. If you swap the agent implementation tomorrow, nothing else changes.

```csharp
// This is the entire public contract.
string response = await agent.InvokeAsync("What's the weather in Tokyo?");
```

### Conversation is the Only State

SK manages `ChatHistory` inside `AgentThread` with `ChatMessageStore` options. ADK maintains `session.state` (a key-value scratchpad), `session.events` (an event timeline), and magic key prefixes (`user:`, `app:`) for cross-session persistence. LangGraph uses typed state graphs with channel-based state management.

AgentCore asks: **why?**

The LLM doesn't read your session state dictionary. It reads messages. The conversation IS the session. The message list IS the memory. There is no separate "state" because there's nothing to track outside the conversation.

This has consequences that matter:

- **The agent is stateless.** `LLMAgent` holds zero mutable state. One instance safely serves thousands of concurrent sessions — no locks, no per-session copies, no state synchronization.
- **Crash recovery is automatic.** The `ToolCallingLoop` saves the conversation after every tool turn. Crash mid-execution? Resume with the same `sessionId` — the agent picks up from the last completed turn. No checkpointer system, no state graph snapshots, no external runtime. Saving a JSON file costs milliseconds; an LLM call costs seconds. The bottleneck makes this trivially cheap.
- **Cross-session knowledge is a tool, not a framework concern.** Need the agent to remember something from 3 months ago? Give it a search tool. That's RAG — it belongs in a tool, not baked into the agent's memory model.

### Everything is Content

`IContent` is the universal primitive. Text, tool calls, tool results, tool errors — everything implements `IContent` with a single method: `ForLlm()`. There is one pipeline, one flow.

When a tool throws an exception, it becomes a `ToolResult` containing the error — just another message the LLM can read and self-correct from. No special error channels, no retry plugins, no `FunctionInvocationFilter`. Errors flow through the same conversation pipeline as everything else.

Fewer code paths means fewer bugs. The LLM doesn't distinguish between a tool result and an error message — why should the framework?

### Middleware is Optional

AgentCore provides a **middleware pipeline** for extensibility, but it's not required. The default execution works without any middleware:

- **LLM middleware**: logging, caching, retries, telemetry
- **Tool middleware**: logging, validation, mocking

The pipeline is there if you need it, but the common case (just use the agent) requires zero middleware.

### An Agent Primitive, Not a Framework

~1,800 lines is not a limitation. It's a statement about how much code a correct agent primitive actually requires. Tool calling, context management, session persistence, crash recovery, typed output, streaming, middleware — all present, all working, all in 1,800 lines.

What's missing isn't missing by accident. Multi-agent orchestration is an application-level concern — you compose it on top. RAG is a tool. Multi-modal is a provider concern. The framework doesn't own these because they don't belong in the primitive.

---

## Quick Start

```csharp
var agent = LLMAgent.Create("my-agent")
    .WithInstructions("You are a helpful assistant.")
    .WithProvider(new OpenAILLMClient(o =>
    {
        o.Model = "gpt-4o";
        o.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    }))
    .WithTools<WeatherTools>()
    .WithTools<MathTools>()
    .Build();

// String in, string out.
var answer = await agent.InvokeAsync("What's 42°F in Celsius?");

// Or stream it.
await foreach (var chunk in agent.InvokeStreamingAsync("Tell me about Tokyo"))
    Console.Write(chunk);
```

### Tool Definition

Mark any method with `[Tool]`:

```csharp
public class WeatherTools
{
    [Tool(description: "Get the current weather for a location")]
    public static string GetWeather(
        [Description("City name")] string location)
    {
        return $"Weather in {location}: 22°C, sunny";
    }
}
```

AgentCore auto-generates JSON schemas from method signatures. Parameters, types, descriptions, required/optional — all inferred. Async methods and `CancellationToken` injection just work.

### Typed Output

```csharp
var agent = LLMAgent.Create("extractor")
    .WithInstructions("Extract structured data from the user's input.")
    .WithProvider(new OpenAILLMClient(o => { o.Model = "gpt-4o"; o.ApiKey = "..."; }))
    .WithOutput<PersonInfo>()
    .Build();

// The model response is constrained to the schema of PersonInfo.
var result = await agent.InvokeAsync("John Doe, 30 years old, lives in NYC.");
```

### Session Persistence & Crash Recovery

Every invocation can have a `sessionId`. The memory system persists every conversation turn to disk, so if your agent crashes mid-session, you can resume exactly where it left off:

```csharp
// First run:
await agent.InvokeAsync("Search for flights to Tokyo", sessionId: "session-abc");

// After crash/restart — resumes from saved state:
await agent.InvokeAsync("Now book the cheapest one", sessionId: "session-abc");
```

### Middleware for Observability & Control

```csharp
var agent = LLMAgent.Create("agent")
    .WithProvider(new OpenAILLMClient(o => { ... }))
    .WithTools<MyTools>()
    .UseLLMMiddleware(async (req, next, ct) =>
    {
        Console.WriteLine($"Calling LLM with {req.Messages.Count} messages");
        return next(req, ct);
    })
    .UseToolMiddleware(async (call, next, ct) =>
    {
        Console.WriteLine($"  [Tool] → {call.Name}({call.Arguments})");
        var result = await next(call, ct);
        Console.WriteLine($"  [Tool] ← {result.Content}");
        return result;
    })
    .Build();
```

---

## Architecture

```
┌────────────────────────────────────────────────────────────────┐
│                     Agent Layer (Public API)                    │
│    IAgent: InvokeAsync(string) → string                        │
│            InvokeStreamingAsync(string) → IAsyncEnumerable     │
│                                                                │
│    LLMAgent orchestrates: Memory → Executor → Memory          │
├────────────────────────────────────────────────────────────────┤
│                   Agent Executor Layer (Control Flow)           │
│    IAgentExecutor: your agent logic lives here                 │
│                                                                │
│    Default: ToolCallingLoop                                    │
│      while (true) {                                           │
│        stream LLM → yield text, collect tool calls             │
│        if no tool calls → break                               │
│        execute tools in parallel → append results → loop      │
│      }                                                         │
├────────────────────────────────────────────────────────────────┤
│                    Middleware Pipeline Layer                   │
│    PipelineHandler<TRequest, TResult>:                        │
│      Executes middleware chain before reaching executor       │
│                                                                │
│    LLM Pipeline: LLMCall → IAsyncEnumerable<LLMEvent>         │
│    Tool Pipeline: ToolCall → Task<ToolResult>                 │
├────────────────────────────────────────────────────────────────┤
│                    LLM Executor Layer (Events)                 │
│    ILLMExecutor: StreamAsync(messages, options) → LLMEvent    │
│                                                                │
│    - Applies context strategy (reduce messages to fit window) │
│    - Calls provider, reassembles streaming deltas             │
│    - Text → streamed as TextEvent immediately                 │
│    - Tool calls → buffered, emitted as ToolCallEvent at end   │
│    - Records token usage                                      │
├────────────────────────────────────────────────────────────────┤
│                    LLM Provider Layer (Raw I/O)                │
│    ILLMProvider: StreamAsync(messages, options, tools)         │
│                  → IAsyncEnumerable<IContentDelta>            │
│                                                                │
│    Raw provider implementation. Yields:                        │
│      TextDelta | ToolCallDelta | MetaDelta                    │
│                                                                │
│    Providers: AgentCore.OpenAI, AgentCore.Gemini               │
└────────────────────────────────────────────────────────────────┘
```

### Layer Separation

| Layer | Knows About | Doesn't Know About |
|---|---|---|
| **Agent** | strings, session IDs, memory | messages, roles, models, tokens |
| **AgentExecutor** | messages, tools, LLM events | providers, context windows, raw deltas |
| **Middleware** | TRequest/TResult types | internal execution details |
| **LLMExecutor** | messages, options, context strategy, token tracking | provider HTTP, SDK details |
| **LLMProvider** | HTTP, SDK, raw streaming | context management, token tracking, tools registry |

---

## Core Components

### Runtime

| File | Lines | Purpose |
|---|---|---|
| `Agent.cs` | ~106 | `IAgent` interface + `LLMAgent` implementation. String-in, string-out. Orchestrates memory recall → executor → memory update. |
| `AgentBuilder.cs` | ~93 | Fluent builder. Wires components, hooks, and options via explicit composition. Builds `LLMAgent`. |
| `AgentExecutor.cs` | ~126 | `IAgentExecutor` interface + `ToolCallingLoop` default. The agent loop. |
| `IAgentContext.cs` | ~28 | Context passed to executor: sessionId, config, messages, userInput, outputType. |
| `AgentMemory.cs` | ~115 | `IAgentMemory` interface + `FileMemory` default. Recall/update/clear with JSON file persistence. |

### LLM

| File | Lines | Purpose |
|---|---|---|
| `LLMExecutor.cs` | ~143 | Consumes raw deltas from provider, emits `TextEvent`/`ToolCallEvent`. Handles context reduction, token tracking. Uses Pipeline middleware. |
| `LLMCall.cs` | ~5 | Simple record: `(IReadOnlyList<Message> Messages, LLMOptions Options)`. No bloat. |
| `ILLMProvider.cs` | ~14 | Single-method interface: `StreamAsync → IAsyncEnumerable<IContentDelta>`. |
| `LLMEvent.cs` | ~9 | Two events: `TextEvent(string Delta)`, `ToolCallEvent(ToolCall Call)`. |
| `LLMOptions.cs` | ~21 | Flat config class: model, API key, base URL, sampling parameters, response schema, context length. |
| `LLMMeta.cs` | ~9 | `FinishReason` enum, `ToolCallMode` enum. |

### Tooling

| File | Lines | Purpose |
|---|---|---|
| `Tool.cs` | ~33 | `[Tool]` attribute + `Tool` class (name, description, JSON schema, delegate). |
| `ToolRegistry.cs` | ~178 | Registration, lookup, auto-schema generation from method signatures. |
| `ToolExecutor.cs` | ~196 | Invocation engine: parameter parsing, validation, CancellationToken injection. Uses Pipeline middleware. |
| `ToolOptions.cs` | ~9 | Config: MaxConcurrency, DefaultTimeout. **No MaxResultLength** — trimming handled by ContextManager. |
| `ToolRegistryExtensions.cs` | ~71 | `RegisterAll<T>()` — discovers `[Tool]` methods from a type via reflection. |

### Execution (Middleware Pipeline)

| File | Lines | Purpose |
|---|---|---|
| `Pipeline.cs` | ~28 | Generic middleware pipeline: `PipelineHandler<TRequest, TResult>` and `PipelineMiddleware<TRequest, TResult>`. |

### Diagnostics

| File | Lines | Purpose |
|---|---|---|
| `AgentTelemetryExtensions.cs` | ~62 | `WithOpenTelemetry()` extension for OpenTelemetry integration via middleware. |
| `AgentDiagnosticSource.cs` | ~8 | `DiagnosticSource` for activity tracking. |

### Tokens

| File | Lines | Purpose |
|---|---|---|
| `ContextManager.cs` | ~9 | `IContextManager` interface. |
| `SummarizingContextManager.cs` | ~95 | Single implementation: tail-trims to fit context. If provider given, summarizes dropped messages. If no provider, just tail-trims. |
| `TokenManager.cs` | ~39 | Cumulative token usage tracking across LLM calls. |
| `ITokenCounter.cs` | ~8 | Interface: `Count(string) → int`. |
| `ApproximateTokenCounter.cs` | ~74 | Default: `length / 4`. Provider packages can register accurate counters (e.g., TikToken for OpenAI). |

### Chat (Internal Primitives)

| File | Lines | Purpose |
|---|---|---|
| `Content.cs` | ~45 | `IContent` interface + `Text`, `ToolCall`, `ToolResult` records. |
| `ContentDelta.cs` | ~17 | `IContentDelta` interface + `TextDelta`, `ToolCallDelta`, `MetaDelta` — raw provider streaming types. |
| `Message.cs` | ~9 | `Message(Role, IContent)` — the internal message representation. |
| `Role.cs` | ~6 | `enum Role { System, Assistant, User, Tool }` |
| `Extensions.cs` | ~184 | Helpers: `AddUser()`, `AddAssistant()`, `Clone()`, `ToJson()`, serialization for providers. |

---

## Providers

Provider packages are thin adapters that implement `ILLMProvider`:

### AgentCore.OpenAI

```csharp
.WithProvider(new OpenAILLMClient(o =>
{
    o.Model = "gpt-4o";
    o.ApiKey = "sk-...";
    o.BaseUrl = "https://api.openai.com/v1"; // or any OpenAI-compatible endpoint
}))
```

- Uses the official `OpenAI` .NET SDK
- Auto-registers `TikTokenCounter` for accurate token counting
- Supports any OpenAI-compatible API (LM Studio, Ollama, Azure, etc.)

### AgentCore.Gemini

```csharp
.WithProvider(new GeminiLLMClient(o =>
{
    o.Model = "gemini-2.0-flash";
    o.ApiKey = "...";
}, project: "my-project", location: "us-central1"))
```

- Uses `Google.Cloud.AIPlatform.V1` SDK
- Supports Vertex AI project/location

### Writing Your Own Provider

Implement `ILLMProvider`:

```csharp
public class MyProvider : ILLMProvider
{
    public async IAsyncEnumerable<IContentDelta> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions options,
        IReadOnlyList<Tool>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Call your LLM API, yield deltas:
        yield return new TextDelta("Hello ");
        yield return new TextDelta("world!");
        yield return new MetaDelta(FinishReason.Stop, new TokenUsage(10, 5));
    }
}

// Register it:
builder.WithProvider(new MyProvider());
```

---

## Memory System

The memory design follows a simple principle: **a session is just the list of messages you feed to the agent.**

```csharp
public interface IAgentMemory
{
    Task<IList<Message>> RecallAsync(string sessionId, string userRequest);
    Task UpdateAsync(string sessionId, string userRequest, string response);
    Task ClearAsync(string sessionId);
}
```

- `RecallAsync` is called **before** execution — loads past conversation
- `UpdateAsync` is called **after** execution — persists the new turn
- That's the entire contract

The default `FileMemory` writes JSON files to `%APPDATA%/AgentCore/{sessionId}.json`. Need RAG? Vector search? Redis? Just implement `IAgentMemory`.

---

## Context Management

The `ContextManager` is the **single source of truth for ALL trimming** — messages and tool results alike. There's one implementation that handles both cases:

### SummarizingContextManager (single implementation)

The only implementation. Two modes:

1. **Without provider (default)**: Tail-trim strategy - keeps all system messages + most recent user/assistant messages that fit within available context. Drops oldest messages until total fits.

2. **With provider**: Same tail-trimming, but when messages must be dropped, summarizes them via an additional LLM call and injects the summary as a "scratchpad" message. More expensive but preserves more context.

### Why No MaxResultLength?

Previous versions had `ToolOptions.MaxResultLength` — a hardcoded cap on tool results. This was removed because:

1. It's a magic number: why 32k? What works for gpt-4 (128k) doesn't work for gpt-3.5-turbo (4k)
2. It's redundant: ContextManager handles ALL trimming
3. It's inflexible: doesn't account for varying context windows

Now: ContextManager calculates available space dynamically based on the model's actual context window.

### Default Behavior

By default, AgentCore uses `SummarizingContextManager` **without a provider** — pure tail-trimming. If you want summarization, provide an LLM provider to the context manager:

```csharp
// Default (tail-trim only):
.WithContextManager(new SummarizingContextManager(tokenCounter, logger))

// With summarization (requires a separate LLM provider):
.WithContextManager(new SummarizingContextManager(tokenCounter, logger, summaryProvider))
```

---

## Middleware Pipeline

AgentCore uses a simple, generic middleware pipeline inspired by ASP.NET Core:

```csharp
public delegate TResult PipelineHandler<in TRequest, out TResult>(TRequest request, CancellationToken ct);
public delegate TResult PipelineMiddleware<TRequest, TResult>(TRequest request, PipelineHandler<TRequest, TResult> next, CancellationToken ct);
```

### LLM Middleware

```csharp
builder.UseLLMMiddleware(async (req, next, ct) =>
{
    // req is LLMCall: (Messages, Options)
    Console.WriteLine($"Calling {req.Options.Model}");
    
    var events = next(req, ct);
    
    // Can transform, log, cache, etc.
    return events;
});
```

### Tool Middleware

```csharp
builder.UseToolMiddleware(async (call, next, ct) =>
{
    // call is ToolCall
    Console.WriteLine($"Executing {call.Name}");
    
    var result = await next(call, ct);
    
    return result;
});
```

### Use Cases

- **Logging/telemetry**: Trace all LLM calls and tool executions
- **Caching**: Cache LLM responses by hash of messages
- **Retries**: Automatic retry with exponential backoff
- **Mocking**: Replace tool results in tests
- **Validation**: Validate tool arguments before execution

---

## Custom Executor

The `IAgentExecutor` interface is intentionally minimal:

```csharp
public interface IAgentExecutor
{
    IAsyncEnumerable<string> ExecuteStreamingAsync(IAgentContext ctx, CancellationToken ct = default);
}
```

The context gives you everything:

```csharp
public class MyExecutor(ILLMExecutor llm, IToolExecutor tools) : IAgentExecutor
{
    public async IAsyncEnumerable<string> ExecuteStreamingAsync(
        IAgentContext ctx, CancellationToken ct = default)
    {
        // Access the config, user input, messages
        var input = ctx.UserInput;
        var messages = ctx.Messages;

        // Build any agent loop you want.
        // ReAct, plan-and-execute, multi-step reasoning, whatever.
    }
}

// Register it:
builder.WithExecutor(new MyExecutor(llmExecutor, toolExecutor));
```

---

## Dependencies

The core `AgentCore` package has exactly **2 dependencies**:

- `Microsoft.Extensions.Logging`
- `System.ComponentModel.Annotations`

That's it. No DI containers, no HTTP clients, no LLM SDKs, no serialization libraries beyond `System.Text.Json`.

---

## Project Structure

```
AgentCore/                          # Core framework (~1,800 lines)
├── Runtime/
│   ├── Agent.cs                    # IAgent, LLMAgent — string in, string out
│   ├── AgentBuilder.cs             # Fluent builder + explicit composition
│   ├── AgentExecutor.cs            # IAgentExecutor, ToolCallingLoop
│   ├── IAgentContext.cs            # Context passed to executor
│   └── AgentMemory.cs              # IAgentMemory, FileMemory
├── LLM/
│   ├── LLMExecutor.cs              # Event-level streaming + middleware
│   ├── LLMCall.cs                  # Simple record: (Messages, Options)
│   ├── ILLMProvider.cs             # Raw provider interface
│   ├── LLMEvent.cs                 # TextEvent, ToolCallEvent
│   ├── LLMOptions.cs              # Model config
│   └── LLMMeta.cs                  # FinishReason, ToolCallMode
├── Tooling/
│   ├── Tool.cs                     # [Tool] attribute + Tool class
│   ├── ToolRegistry.cs             # Registration + auto-schema
│   ├── ToolExecutor.cs             # Invocation + middleware
│   ├── ToolOptions.cs              # MaxConcurrency, DefaultTimeout
│   └── ToolRegistryExtensions.cs  # RegisterAll<T>() reflection
├── Execution/
│   └── Pipeline.cs                 # Middleware pipeline
├── Diagnostics/
│   ├── AgentTelemetryExtensions.cs # OpenTelemetry integration
│   └── AgentDiagnosticSource.cs    # Diagnostic source
├── Tokens/
│   ├── ContextManager.cs           # IContextManager interface only
│   ├── SummarizingContextManager.cs # Single implementation: tail-trim + optional summarize
│   ├── TokenManager.cs             # Cumulative token tracking
│   ├── ITokenCounter.cs            # Counter interface
│   └── ApproximateTokenCounter.cs  # len/4 fallback
├── Chat/
│   ├── Content.cs                  # IContent, Text, ToolCall, ToolResult
│   ├── ContentDelta.cs             # Raw streaming delta types
│   ├── Message.cs                  # Message(Role, IContent)
│   ├── Role.cs                     # System, Assistant, User, Tool
│   └── Extensions.cs               # Conversation helpers + serialization

AgentCore.OpenAI/                   # OpenAI provider package
├── OpenAILLMClient.cs              # ILLMProvider implementation
├── OpenAIExtensions.cs             # Message/tool conversion helpers
├── OpenAIServiceExtensions.cs      # .AddOpenAI() builder extension
└── TikTokenCounter.cs              # Accurate token counting

AgentCore.Gemini/                   # Gemini provider package
├── GeminiLLMClient.cs              # ILLMProvider implementation
├── GeminiExtensions.cs             # Message/tool conversion helpers
└── GeminiServiceExtensions.cs      # .AddGemini() builder extension
```

---

## Execution Flow

```
User calls agent.InvokeAsync("task") or agent.InvokeStreamingAsync("task")
  │
  ├── Generate session ID (if not provided)
  ├── memory.RecallAsync(sessionId) → load past messages
  ├── Build AgentContext (config, input)
  ├── Add system prompt to messages
  │
  ├── executor.ExecuteStreamingAsync(ctx)    ← your agent logic
  │     │
  │     ├── Add user message to messages
  │     │
  │     └── LOOP:
  │           ├── llmExecutor.StreamAsync(messages, options)
  │           │     │
  │           │     ├── LLM Middleware Pipeline
  │           │     │
  │           │     ├── contextManager.Reduce(messages)    ← ALL trimming
  │           │     │     • Calculates available context
  │           │     │     • Trims messages + tool results uniformly
  │           │     │
  │           │     ├── provider.StreamAsync(...)           ← raw API call
  │           │     ├── Reassemble deltas → TextEvent / ToolCallEvent
  │           │     └── tokenManager.Record(usage)
  │           │
  │           ├── yield TextEvent deltas to caller (streaming)
  │           │
  │           ├── if tool calls:
  │           │     ├── Tool Middleware Pipeline
  │           │     ├── toolExecutor.ExecuteAsync(call)
  │           │     ├── append results to messages
  │           │     └── continue loop
  │           │
  │           └── if no tool calls → break (final response)
  │
  └── memory.UpdateAsync(sessionId, input, response) → persist
```

---

## Full Example

```csharp
using AgentCore;
using AgentCore.Providers.OpenAI;

var agent = LLMAgent.Create("chatbot")
    .WithInstructions("You are an AI agent. Execute all user requests faithfully.")
    .WithProvider(new OpenAILLMClient(o =>
    {
        o.BaseUrl = "http://127.0.0.1:1234/v1";
        o.ApiKey = "lmstudio";
        o.Model = "model";
    }))
    .WithTools<WeatherTools>()
    .WithTools<MathTools>()
    .UseToolMiddleware(async (call, next, ct) =>
    {
        Console.WriteLine($"  [Tool] → {call.Name}({call.Arguments})");
        return await next(call, ct);
    })
    .Build();

// Interactive chat loop with session persistence
var sessionId = "my-session";
while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) continue;

    await foreach (var chunk in agent.InvokeStreamingAsync(input, sessionId))
        Console.Write(chunk);

    Console.WriteLine();
}
```

---

## License

MIT
