# AgentCore

AgentCore is a minimal agent framework for .NET. It acts as a pure execution engine: pass in a task, get back a result. Inside, it manages the full agent loop — context, tool execution, provider coordination — without any of it leaking into your code.

~1,800 lines. 2 dependencies. Everything an agent needs. Nothing it doesn't.

---

## The Philosophy

Most agent frameworks solve problems they created. They introduce massive state graphs, then need CheckpointSavers. They build complex memory abstractions, then need semantic memory lifecycle hooks. They merge LLM configurations directly into Agent orchestration, leaking "how it thinks" into "what it does."

AgentCore provides a fundamentally different bedrock.

### 1. One content model, strict boundaries
All events in an agent's life — user input, model response, a tool call, a tool result, a tool error — are treated as the same kind of thing: `IContent`. There is one pipeline. This means no special error channels or repair hacks. If a tool fails, it's just a message the model reads to self-correct.

Crucially, the layers are strictly separated. The LLM layer handles tokens and network boundaries; it never leaks provider parameters to the Agent layer. The orchestration layer only receives *completed* concerns.

### 2. The loop does exactly one job
The agent loop streams from the LLM, dispatches tool calls, appends results, and checkpoints state. That's all it does. Context reduction and token tracking are handled by dedicated layers the loop simply calls. 

Because the loop inherently pauses at `Task.WhenAll` to await tools, durability is a natural byproduct. Combined with the default `FileMemory`, AgentCore provides perfect, stateless crash recovery per session ID without a massive lifecycle manager or dedicated database thread.

### 3. Middleware is the extension model
There are three dedicated functional middleware pipelines — Agent, LLM, and Tool level. Not added for extensibility or as ergonomic bolts-on — they are the core extension model. You get full interception and observability at every meaningful boundary without touching anything internal.

### 4. Opinionated Context & Token Calibration
Tail-trimming context windows creates "Amnesia Agents." Exact token counting requires synchronous Tiktoken dependencies that tank performance.

AgentCore avoids this by default:
- It uses a LangChain-inspired `ApproximateTokenCounter` that dynamically calibrates based on actual network responses, remaining incredibly fast while self-correcting drift.
- It defaults to a `SummarizingContextManager` that uses recursive summarization to gracefully fold dropped history into a context scratchpad at the boundary right before the LLM fires, completely decoupled from the true persistent AgentMemory.

---

## What You Build On Top

Because AgentCore is a completely stateless primitive, building complex topologies like multi-agent workflows or routing isn't a framework feature you have to wait for — it's just your code orchestrating the engine.

```csharp
var supervisor = LLMAgent.Create("router")
    .WithInstructions("Route the user to the correct expert.")
    .WithTools<RoutingTools>() 
    .Build();

var worker = LLMAgent.Create("researcher")
    .WithTools<SearchTools>()
    .Build();

// It's just C# orchestration
var decision = await supervisor.InvokeAsync<RouteDecision>("I need to research Quantum Physics.");

if (decision.Target == "worker") 
{
    // The worker executes statelessly
    var result = await worker.InvokeAsync(decision.Query, sessionId: "req-123");
}
```

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

Mark any method with `[Tool]`. AgentCore uses C# reflection to automatically infer `JsonSchema` parameters and generate JSON-compatible execution delegates. Zero definition drift.

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

### Typed Output

```csharp
var agent = LLMAgent.Create("extractor")
    .WithInstructions("Extract structured data from the user's input.")
    .WithProvider(new OpenAILLMClient(o => { o.Model = "gpt-4o"; o.ApiKey = "..."; }))
    .WithOutput<PersonInfo>() // Force structured extraction
    .Build();

// The model response is constrained to the schema of PersonInfo.
var result = await agent.InvokeAsync<PersonInfo>("John Doe, 30 years old.");
```

### Session Persistence & Crash Recovery

Every invocation can have a `sessionId`. The memory system persists the transcript, so if your agent crashes mid-session, you can resume exactly where it left off.

```csharp
// First run:
await agent.InvokeAsync("Search for flights to Tokyo", sessionId: "session-abc");

// After crash/restart — AgentCore loads the transcript and resumes:
await agent.InvokeAsync("Now book the cheapest one", sessionId: "session-abc");
```

### Middleware for Observability

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

| AgentCore handles | You handle |
|---|---|
| Agent invocation loop | Multi-agent topology |
| Execution history state | Handoff and routing logic |
| Context trimming / counting | Approval and business flows |
| Tool execution + errors | External side-effects & state |
| Middleware pipelines | Orchestration workflow |
| Session persistence | App-specific user sessions |

### Layer Separation

```
┌────────────────────────────────────────────────────────────────┐
│                     Agent Layer (Public API)                    │
│    IAgent: InvokeAsync<T>(string) → T                          │
│            InvokeStreamingAsync(string) → IAsyncEnumerable     │
│                                                                │
│    Orchestrates: Memory → Executor → Memory                    │
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
│    - Records token usage via ApproximateTokenCounter          │
├────────────────────────────────────────────────────────────────┤
│                    LLM Provider Layer (Raw I/O)                │
│    ILLMProvider: StreamAsync(messages, options, tools)         │
│                  → IAsyncEnumerable<IContentDelta>            │
│                                                                │
│    Raw provider implementation. Yields:                        │
│      TextDelta | ToolCallDelta | MetaDelta                    │
│                                                                │
│    Providers: AgentCore.OpenAI, AgentCore.MEAI                 │
└────────────────────────────────────────────────────────────────┘
```

| Layer | Knows About | Doesn't Know About |
|---|---|---|
| **Agent** | strings, session IDs, memory | messages, roles, models, tokens |
| **AgentExecutor** | messages, tools, LLM events | providers, context windows, raw deltas |
| **Middleware** | TRequest/TResult types | internal execution details |
| **LLMExecutor** | messages, context strategy, tokens | provider HTTP, network limits, SDKs |
| **LLMProvider** | HTTP, SDK, raw streaming limits | context management, agent memory |

---

## Memory System

The memory design follows a simple principle: **the transcript IS the session.**

```csharp
public interface IAgentMemory
{
    Task<IList<Message>> RecallAsync(string sessionId, string userRequest);
    Task UpdateAsync(string sessionId, IList<Message> messages);
    Task ClearAsync(string sessionId);
}
```

- `RecallAsync` loads the history before execution.
- `UpdateAsync` persists the transcript during/after execution.

The default `FileMemory` writes JSON transcripts safely to disk. Need RAG? Vector search? Just implement `IAgentMemory`.

---

## Providers

Provider packages are very thin adapters that implement `ILLMProvider`. Because the framework handles context reduction, schema generation, and tooling workflows natively, integrating new models takes roughly ~100 lines of code.

### AgentCore.OpenAI

Uses the official `OpenAI` .NET SDK. Supports any OpenAI-compatible API (LM Studio, Ollama, Azure, etc.)

```csharp
.WithProvider(new OpenAILLMClient(o =>
{
    o.Model = "gpt-4o";
    o.ApiKey = "sk-...";
}))
```

### AgentCore.MEAI

Uses the official `Microsoft.Extensions.AI` abstractions. This means AgentCore natively supports **every provider Microsoft supports**, including:
- Azure OpenAI & Local OpenAI
- Google Gemini 
- Anthropic Claude
- Mistral AI
- Local Models (Ollama, LM Studio)
- ...and any other `.AddChatClient()` package.

```csharp
// myChatClient implements IChatClient from MEAI
.WithProvider(new MEAILLMClient(myChatClient))
```

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
| `ToolOptions.cs` | ~9 | Config: MaxConcurrency, DefaultTimeout. Defaults to framework trimming. |
| `ToolRegistryExtensions.cs` | ~71 | `RegisterAll<T>()` — discovers `[Tool]` methods from a type via reflection. |

### Execution (Middleware Pipeline)

| File | Lines | Purpose |
|---|---|---|
| `Pipeline.cs` | ~28 | Generic middleware pipeline: `PipelineHandler<TRequest, TResult>` and `PipelineMiddleware<TRequest, TResult>`. |

### Tokens

| File | Lines | Purpose |
|---|---|---|
| `ContextManager.cs` | ~9 | `IContextManager` interface. |
| `SummarizingContextManager.cs` | ~95 | Single implementation: tail-trims to fit context. If provider given, summarizes dropped messages. If no provider, just tail-trims. |
| `TokenManager.cs` | ~39 | Cumulative token usage tracking across LLM calls. |
| `ITokenCounter.cs` | ~8 | Interface: `CountAsync(messages) → int`. |
| `ApproximateTokenCounter.cs` | ~74 | Default `len/4` fallback + Dynamic Response Calibration |

### Chat (Internal Primitives)

| File | Lines | Purpose |
|---|---|---|
| `Content.cs` | ~45 | `IContent` interface + `Text`, `ToolCall`, `ToolResult` records. |
| `ContentDelta.cs` | ~17 | `IContentDelta` interface + `TextDelta`, `ToolCallDelta`, `MetaDelta` — raw provider streaming types. |
| `Message.cs` | ~9 | `Message(Role, IContent)` — the internal message representation. |
| `Role.cs` | ~6 | `enum Role { System, Assistant, User, Tool }` |
| `Extensions.cs` | ~184 | Helpers: `AddUser()`, `AddAssistant()`, `Clone()`, `ToJson()`, serialization for providers. |

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
│   └── ApproximateTokenCounter.cs  # Dynamic length approximation
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

AgentCore.MEAI/                     # MEAI provider package
├── MEAILLMClient.cs                # ILLMProvider implementation
├── MEAIExtensions.cs               # Message/tool conversion helpers
└── MEAIServiceExtensions.cs        # .AddMEAI() builder extension
```

---

## Dependencies

The core `AgentCore` package has exactly **2 dependencies**:

- `Microsoft.Extensions.Logging`
- `System.ComponentModel.Annotations`

No DI containers. No bloated abstractions. Just the primitive.
