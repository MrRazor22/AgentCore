# AgentCore

A minimal, un-opinionated agent framework for .NET.

Build any agent product you want — from a simple chatbot to a complex autonomous system — without digging through layers of abstraction. Inspired by [smolagents](https://github.com/huggingface/smolagents): the entire core is ~1,400 lines of code, zero opinions, zero magic.

---

## Design Philosophy

### Agent is a Layer, Not a Wrapper

Most agent frameworks (Semantic Kernel, AutoGen, LangChain, Google ADK) expose LLM-level primitives — chat messages, roles, model options — directly at the agent level. The agent becomes a thin wrapper around the LLM API. 

AgentCore disagrees. **An agent is a higher-level abstraction than an LLM call.** At the agent surface:

- **Input:** a task, as a `string`
- **Output:** a final response, as a `string`

That's it. How the agent talks to the model, manages context, calls tools, retries — these are _internal concerns_, not things the caller should worry about.

```csharp
// This is the entire public contract of an agent.
string response = await agent.InvokeAsync("What's the weather in Tokyo?");
```

### Executor = Sandbox for Your Agent Logic

The `AgentExecutor` is where the agent loop lives. The default `ToolCallingLoop` is the standard tool-calling agent loop in ~60 lines. But you can replace it with anything — ReAct, plan-and-execute, chain-of-thought, multi-agent delegation. Think of it as a sandbox: you get the full power of the framework's services (LLM, tools, memory, tokens) and you write whatever control flow you want.

### Sensible Defaults, Total Replaceability

Every component has a default that works out of the box:

| Concern | Default | Interface |
|---|---|---|
| Agent loop | `ToolCallingLoop` | `IAgentExecutor` |
| Memory | `FileMemory` (JSON persistence) | `IAgentMemory` |
| Context strategy | `ContextManager` (tail-trim) | `IContextManager` |
| Token counting | `ApproximateTokenCounter` (len/4) | `ITokenCounter` |
| Token tracking | `TokenManager` | `ITokenManager` |

Don't like any of them? Implement the interface, register it via DI. No base classes to inherit, no policies to configure.

### No Middleware, No Filters — Hooks

Instead of middleware pipelines or filter chains, AgentCore gives you **four hooks**:

- `BeforeToolCall` / `AfterToolCall` — intercept tool execution
- `BeforeModelCall` / `AfterModelCall` — intercept LLM calls

Each hook can observe, short-circuit (return a cached result), or replace the result. That's the entire controllability surface. If you need more, write your own executor.

---

## Quick Start

```csharp
var agent = LLMAgent.Create("my-agent")
    .WithInstructions("You are a helpful assistant.")
    .AddOpenAI(o =>
    {
        o.Model = "gpt-4o";
        o.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    })
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
    .AddOpenAI(o => { o.Model = "gpt-4o"; o.ApiKey = "..."; })
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

### Hooks for Observability & Control

```csharp
var agent = LLMAgent.Create("agent")
    .AddOpenAI(o => { ... })
    .WithTools<MyTools>()
    .BeforeToolCall(async (call, ct) =>
    {
        Console.WriteLine($"Calling: {call.Name}({call.Arguments})");
        return null; // null = proceed normally; return IContent to short-circuit
    })
    .AfterToolCall(async (call, result, ct) =>
    {
        Console.WriteLine($"Result: {result?.ForLlm()}");
        return null; // null = use original result; return IContent to replace
    })
    .AfterModelCall(async (events, ct) =>
    {
        var toolCalls = events.OfType<ToolCallEvent>().Count();
        Console.WriteLine($"Model returned {events.Count} events, {toolCalls} tool calls");
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
│    LLMAgent orchestrates: Memory → Executor → Memory           │
├────────────────────────────────────────────────────────────────┤
│                   Agent Executor Layer (Sandbox)                │
│    IAgentExecutor: your agent logic lives here                 │
│                                                                │
│    Default: ToolCallingLoop                                    │
│      while (true) {                                            │
│        stream LLM → yield text, collect tool calls             │
│        if no tool calls → break                                │
│        execute tools in parallel → append results → loop       │
│      }                                                         │
├────────────────────────────────────────────────────────────────┤
│                    LLM Executor Layer (Events)                  │
│    ILLMExecutor: StreamAsync(messages, options) → LLMEvent     │
│                                                                │
│    - Applies context strategy (reduce messages to fit window)  │
│    - Calls provider, reassembles streaming deltas              │
│    - Text → streamed as TextEvent immediately                  │
│    - Tool calls → buffered, emitted as ToolCallEvent at end   │
│    - Records token usage                                       │
│    - Fires Before/After hooks                                  │
├────────────────────────────────────────────────────────────────┤
│                    LLM Provider Layer (Raw I/O)                 │
│    ILLMProvider: StreamAsync(messages, options, tools)          │
│                  → IAsyncEnumerable<IContentDelta>             │
│                                                                │
│    Raw provider implementation. Yields:                        │
│      TextDelta | ToolCallDelta | MetaDelta                     │
│                                                                │
│    Providers: AgentCore.OpenAI, AgentCore.Gemini               │
└────────────────────────────────────────────────────────────────┘
```

### Layer Separation

| Layer | Knows About | Doesn't Know About |
|---|---|---|
| **Agent** | strings, session IDs, memory | messages, roles, models, tokens |
| **AgentExecutor** | messages, tools, LLM events | providers, context windows, raw deltas |
| **LLMExecutor** | messages, options, context strategy, token tracking | provider HTTP, SDK details |
| **LLMProvider** | HTTP, SDK, raw streaming | context management, token tracking, tools registry |

---

## Core Components

### Runtime

| File | Lines | Purpose |
|---|---|---|
| `Agent.cs` | ~93 | `IAgent` interface + `LLMAgent` implementation. String-in, string-out. Orchestrates memory recall → executor → memory update. |
| `AgentBuilder.cs` | ~90 | Fluent builder. Registers DI services, wires hooks, builds `LLMAgent`. |
| `AgentExecutor.cs` | ~78 | `IAgentExecutor` interface + `ToolCallingLoop` default. The agent loop sandbox. |
| `AgentMemory.cs` | ~83 | `IAgentMemory` interface + `FileMemory` default. Recall/update/clear with JSON file persistence. |

### LLM

| File | Lines | Purpose |
|---|---|---|
| `LLMExecutor.cs` | ~123 | Consumes raw deltas from provider, emits `TextEvent`/`ToolCallEvent`. Handles context reduction, token tracking, before/after hooks. |
| `ILLMProvider.cs` | ~15 | Single-method interface: `StreamAsync → IAsyncEnumerable<IContentDelta>`. |
| `LLMEvent.cs` | ~10 | Two events: `TextEvent(string Delta)`, `ToolCallEvent(ToolCall Call)`. |
| `LLMOptions.cs` | ~23 | Flat config class: model, API key, base URL, sampling parameters, response schema. |
| `LLMMeta.cs` | ~10 | `FinishReason` enum, `ToolCallMode` enum. |

### Tooling

| File | Lines | Purpose |
|---|---|---|
| `Tool.cs` | ~30 | `[Tool]` attribute + `Tool` class (name, description, JSON schema, delegate). |
| `ToolRegistry.cs` | ~117 | Registration, lookup, auto-schema generation from method signatures. |
| `ToolExecutor.cs` | ~191 | Invocation engine: parameter parsing, validation, CancellationToken injection, before/after hooks. |
| `ToolCallParser.cs` | ~30 | Fallback: extracts tool calls from text responses when model doesn't use structured tool calling. |
| `ToolRegistryExtensions.cs` | ~86 | `RegisterAll<T>()` — discovers `[Tool]` methods from a type via reflection. |

### Tokens

| File | Lines | Purpose |
|---|---|---|
| `ContextManager.cs` | ~91 | Tail-trim strategy: keeps system prompt + most recent N user/assistant messages that fit the context window. |
| `TokenManager.cs` | ~40 | Cumulative token usage tracking across LLM calls. |
| `ITokenCounter.cs` | ~7 | Interface: `Count(string) → int`. |
| `ApproximateTokenCounter.cs` | ~11 | Default fallback: `length / 4`. Provider packages can register accurate counters (e.g., TikToken for OpenAI). |

### Chat (Internal Primitives)

| File | Lines | Purpose |
|---|---|---|
| `Content.cs` | ~46 | `IContent` interface + `Text`, `ToolCall`, `ToolResult` records. |
| `ContentDelta.cs` | ~18 | `IContentDelta` interface + `TextDelta`, `ToolCallDelta`, `MetaDelta` — raw provider streaming types. |
| `Message.cs` | ~25 | `Message(Role, IContent)` — the internal message representation. |
| `Role.cs` | ~7 | `enum Role { System, Assistant, User, Tool }` |
| `Extensions.cs` | ~180 | Helpers: `AddUser()`, `AddAssistant()`, `Clone()`, `ToJson()`, serialization for providers. |

### JSON

| File | Purpose |
|---|---|
| `JsonSchemaBuilder.cs` | Fluent JSON Schema construction. |
| `JsonSchemaExtensions.cs` | Auto-generates JSON Schema from .NET types + validation. |
| `JsonExtensions.cs` | JSON parsing utilities. |

---

## Providers

Provider packages are thin adapters that implement `ILLMProvider`:

### AgentCore.OpenAI

```csharp
.AddOpenAI(o =>
{
    o.Model = "gpt-4o";
    o.ApiKey = "sk-...";
    o.BaseUrl = "https://api.openai.com/v1"; // or any OpenAI-compatible endpoint
})
```

- Uses the official `OpenAI` .NET SDK
- Auto-registers `TikTokenCounter` for accurate token counting
- Supports any OpenAI-compatible API (LM Studio, Ollama, Azure, etc.)

### AgentCore.Gemini

```csharp
.AddGemini(o =>
{
    o.Model = "gemini-2.0-flash";
    o.ApiKey = "...";
}, project: "my-project", location: "us-central1")
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
        CancellationToken ct = default)
    {
        // Call your LLM API, yield deltas:
        yield return new TextDelta("Hello ");
        yield return new TextDelta("world!");
        yield return new MetaDelta(FinishReason.Stop, new TokenUsage(10, 5));
    }
}

// Register it:
builder.Services.AddSingleton<ILLMProvider, MyProvider>();
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

The `ContextManager` uses a **tail-trim strategy** — when the conversation exceeds the model's context window:

1. Keep all system messages
2. Keep the last N user/assistant message pairs
3. Drop oldest messages until it fits

This is correct for the vast majority of use cases. If you need something different, implement `IContextManager`.

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
public class MyExecutor : IAgentExecutor
{
    public async IAsyncEnumerable<string> ExecuteStreamingAsync(
        IAgentContext ctx, CancellationToken ct = default)
    {
        // Access all framework services
        var llm = ctx.Services.GetRequiredService<ILLMExecutor>();
        var tools = ctx.Services.GetRequiredService<IToolExecutor>();

        // Access the config, user input, scratchpad
        var input = ctx.UserInput;
        var messages = ctx.ScratchPad;

        // Build any agent loop you want.
        // ReAct, plan-and-execute, multi-step reasoning, whatever.
    }
}

// Register it:
builder.ConfigureServices(s =>
    s.AddTransient<IAgentExecutor, MyExecutor>());
```

---

## Dependencies

The core `AgentCore` package has exactly **3 dependencies**:

- `Microsoft.Extensions.DependencyInjection`
- `Microsoft.Extensions.Logging`
- `System.ComponentModel.Annotations`

That's it. No HTTP clients, no LLM SDKs, no serialization libraries beyond `System.Text.Json`.

---

## Project Structure

```
AgentCore/                          # Core framework (~1,400 lines)
├── Runtime/
│   ├── Agent.cs                    # IAgent, LLMAgent — string in, string out
│   ├── AgentBuilder.cs             # Fluent builder + DI wiring
│   ├── AgentExecutor.cs            # IAgentExecutor, ToolCallingLoop
│   └── AgentMemory.cs              # IAgentMemory, FileMemory
├── LLM/
│   ├── LLMExecutor.cs              # Event-level streaming orchestrator
│   ├── ILLMProvider.cs             # Raw provider interface
│   ├── LLMEvent.cs                 # TextEvent, ToolCallEvent
│   ├── LLMOptions.cs               # Model config
│   └── LLMMeta.cs                  # FinishReason, ToolCallMode
├── Tooling/
│   ├── Tool.cs                     # [Tool] attribute + Tool class
│   ├── ToolRegistry.cs             # Registration + auto-schema
│   ├── ToolExecutor.cs             # Invocation + validation
│   ├── ToolCallParser.cs           # Text-based tool call extraction
│   └── ToolRegistryExtensions.cs   # RegisterAll<T>() reflection
├── Tokens/
│   ├── ContextManager.cs           # Tail-trim context strategy
│   ├── TokenManager.cs             # Cumulative token tracking
│   ├── ITokenCounter.cs            # Counter interface
│   └── ApproximateTokenCounter.cs  # len/4 fallback
├── Chat/
│   ├── Content.cs                  # IContent, Text, ToolCall, ToolResult
│   ├── ContentDelta.cs             # Raw streaming delta types
│   ├── Message.cs                  # Message(Role, IContent)
│   ├── Role.cs                     # System, Assistant, User, Tool
│   └── Extensions.cs               # Conversation helpers + serialization
├── Json/
│   ├── JsonSchemaBuilder.cs        # Schema construction
│   ├── JsonSchemaExtensions.cs     # Type → schema + validation
│   └── JsonExtensions.cs           # Parse utilities
└── Utils/
    └── HelperExtensions.cs         # String helpers

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
  ├── Create DI scope
  ├── Generate session ID (if not provided)
  ├── memory.RecallAsync(sessionId) → load past messages
  ├── Build AgentContext (config, services, input, scratchpad)
  ├── Add system prompt to scratchpad
  │
  ├── executor.ExecuteStreamingAsync(ctx)    ← your agent logic
  │     │
  │     ├── Add user message to scratchpad
  │     │
  │     └── LOOP:
  │           ├── llmExecutor.StreamAsync(messages, options)
  │           │     ├── contextManager.Reduce(messages)    ← tail-trim
  │           │     ├── [BeforeModelCall hook]
  │           │     ├── provider.StreamAsync(...)           ← raw API call
  │           │     ├── Reassemble deltas → TextEvent / ToolCallEvent
  │           │     ├── tokenManager.Record(usage)
  │           │     └── [AfterModelCall hook]
  │           │
  │           ├── yield TextEvent deltas to caller (streaming)
  │           │
  │           ├── if tool calls:
  │           │     ├── [BeforeToolCall hook]
  │           │     ├── toolExecutor.HandleToolCallAsync(call)
  │           │     ├── [AfterToolCall hook]
  │           │     ├── append results to scratchpad
  │           │     └── continue loop
  │           │
  │           └── if no tool calls → break (final response)
  │
  └── memory.UpdateAsync(sessionId, input, response) → persist
```

---

## Full Example

```csharp
using AgentCore.Runtime;
using AgentCore.Providers.OpenAI;

var agent = LLMAgent.Create("chatbot")
    .WithInstructions("You are an AI agent. Execute all user requests faithfully.")
    .AddOpenAI(o =>
    {
        o.BaseUrl = "http://127.0.0.1:1234/v1";
        o.ApiKey = "lmstudio";
        o.Model = "model";
    })
    .WithTools<WeatherTools>()
    .WithTools<MathTools>()
    .BeforeToolCall(async (call, ct) =>
    {
        Console.WriteLine($"  [Tool] → {call.Name}({call.Arguments})");
        return null;
    })
    .AfterToolCall(async (call, result, ct) =>
    {
        Console.WriteLine($"  [Tool] ← {result?.ForLlm()}");
        return null;
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
