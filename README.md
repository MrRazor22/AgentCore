# AgentCore

AgentCore is a minimal agent framework for .NET. It acts as a pure execution engine: pass in a task, get back a result. Inside, it manages the full agent loop — context, tool execution, provider coordination — without any of it leaking into your code.

~2,000 lines. 2 dependencies. Everything an agent needs. Nothing it doesn't.

---

## The Philosophy

Most agent frameworks solve problems they created. They introduce massive state graphs, then need CheckpointSavers. They build complex memory abstractions, then need semantic memory lifecycle hooks. They merge LLM configurations directly into Agent orchestration, leaking "how it thinks" into "what it does."

AgentCore provides a fundamentally different bedrock.

### 1. One content model, strict boundaries
All events in an agent's life — user input, model response, a tool call, a tool result, a tool error, reasoning/thoughts — are treated as the same kind of thing: `IContent`. There is one pipeline. This means no special error channels or repair hacks. If a tool fails, it's just a message the model reads to self-correct.

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
- When context limits are exceeded, it proactively triggers summarization and retries the request automatically.

---

## Quick Start

```csharp
var agent = LLMAgent.Create("my-agent")
    .WithInstructions("You are a helpful assistant.")
    .AddOpenAI("gpt-4o", apiKey: Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
    .WithTools<WeatherTools>()
    .WithTools<MathTools>()
    .Build();

// Returns AgentResponse with text, messages, and token usage
var response = await agent.InvokeAsync("What's 42°F in Celsius?");
Console.WriteLine(response.Text);
Console.WriteLine($"Tokens used: {response.Usage.Total}");

// Or stream it
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
    .AddOpenAI("gpt-4o", apiKey: "...")
    .WithOutput<PersonInfo>()
    .Build();

var result = await agent.InvokeAsync<PersonInfo>("John Doe, 30 years old.");
```

### Session Persistence & Crash Recovery

Every invocation can have a `sessionId`. The memory system persists the transcript, so if your agent crashes mid-session, you can resume exactly where it left off.

```csharp
var memory = new FileMemory(new() { PersistDir = @"./memory" });

var agent = LLMAgent.Create("my-agent")
    .WithMemory(memory)
    ...
    .Build();

// First run:
await agent.InvokeAsync("Search for flights to Tokyo", sessionId: "session-abc");

// After crash/restart — AgentCore loads the transcript and resumes:
await agent.InvokeAsync("Now book the cheapest one", sessionId: "session-abc");
```

### Streaming Events

AgentCore streams rich events including text, reasoning thoughts, tool calls, and metadata:

```csharp
await foreach (var evt in agent.InvokeStreamingAsync(input, sessionId))
{
    switch (evt)
    {
        case ReasoningEvent r:
            Console.WriteLine($"💭 {r.Delta}");
            break;
        case TextEvent t:
            Console.Write(t.Delta);
            break;
        case ToolCallEvent tc:
            Console.WriteLine($"🔧 Calling {tc.Call.Name}");
            break;
        case LLMMetaEvent meta:
            Console.WriteLine($"📊 Tokens: {meta.Usage.InputTokens + meta.Usage.OutputTokens}");
            break;
    }
}
```

### Agent Response

The `InvokeAsync` method returns an `AgentResponse` that contains the complete result of the agent invocation:

```csharp
var response = await agent.InvokeAsync("What's the weather in Tokyo?");

// Access text directly (convenience property)
Console.WriteLine(response.Text);

// Access all messages from this turn
foreach (var msg in response.Messages)
{
    Console.WriteLine($"{msg.Role}: {msg.Contents.FirstOrDefault()}");
}

// Get detailed token usage (includes reasoning tokens)
Console.WriteLine($"Input: {response.Usage.InputTokens}");
Console.WriteLine($"Output: {response.Usage.OutputTokens}");
Console.WriteLine($"Reasoning: {response.Usage.ReasoningTokens}");
Console.WriteLine($"Total: {response.Usage.Total}");
```

### Middleware for Observability

```csharp
var agent = LLMAgent.Create("agent")
    .AddOpenAI("gpt-4o", "...")
    .WithTools<MyTools>()
    .UseLLLMiddleware(async (req, next, ct) =>
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
│    IAgent: InvokeAsync(string) → string                        │
│            InvokeAsync<T>(string) → T                          │
│            InvokeStreamingAsync(string) → IAsyncEnumerable     │
│                                                                │
│    Orchestrates: Memory → Executor → Memory                    │
├────────────────────────────────────────────────────────────────┤
│                    Agent Executor Layer (Control Flow)          │
│    CoreStreamAsync:                                            │
│      - Handles the full agent loop                             │
│      - Manages tool call iteration                             │
│      - Context length exceeded → proactive summarization      │
│      - Yields: TextEvent, ReasoningEvent, ToolCallEvent       │
├────────────────────────────────────────────────────────────────┤
│                    Middleware Pipeline Layer                    │
│    PipelineHandler<TRequest, TResult>:                         │
│      Executes middleware chain before reaching executor        │
│                                                                │
│    LLM Pipeline: LLMCall → IAsyncEnumerable<LLMEvent>         │
│    Tool Pipeline: ToolCall → Task<ToolResult>                 │
├────────────────────────────────────────────────────────────────┤
│                    LLM Executor Layer (Events)                 │
│    ILLMExecutor: StreamAsync(messages, options) → LLMEvent   │
│                                                                │
│    - Applies context strategy (reduce messages to fit window) │
│    - Calls provider, reassembles streaming deltas              │
│    - Text → streamed as TextEvent immediately                 │
│    - Reasoning → streamed as ReasoningEvent                  │
│    - Tool calls → buffered, emitted as ToolCallEvent at end   │
│    - Records token usage via ApproximateTokenCounter           │
├────────────────────────────────────────────────────────────────┤
│                    LLM Provider Layer (Raw I/O)                │
│    ILLMProvider: StreamAsync(messages, options, tools)        │
│                  → IAsyncEnumerable<IContentDelta>            │
│                                                                │
│    Raw provider implementation. Yields:                        │
│      TextDelta | ToolCallDelta | ReasoningDelta | MetaDelta   │
│                                                                │
│    Providers: AgentCore.MEAI (OpenAI, Ollama, Anthropic...)   │
└────────────────────────────────────────────────────────────────┘
```

| Layer | Knows About | Doesn't Know About |
|---|---|---|
| **Agent** | IContent, session IDs, memory | messages, roles, models, tokens |
| **Middleware** | TRequest/TResult types | internal execution details |
| **LLMExecutor** | messages, context strategy, tokens | provider HTTP, network limits, SDKs |
| **LLMProvider** | HTTP, SDK, raw streaming limits | context management, agent memory |

---

## Memory System

The memory design follows a simple principle: **the transcript IS the session.**

```csharp
public interface IAgentMemory
{
    Task<List<Message>> RecallAsync(string sessionId);
    Task UpdateAsync(string sessionId, List<Message> chat);
    Task ClearAsync(string sessionId);
    Task<IReadOnlyList<string>> GetAllSessionsAsync();
}
```

- `RecallAsync` loads the history before execution.
- `UpdateAsync` persists the transcript during/after execution.
- `GetAllSessionsAsync` lists all persisted sessions.

The default `FileMemory` writes JSON transcripts safely to disk with atomic writes. There's also an `InMemoryMemory` for testing. Need RAG? Vector search? Just implement `IAgentMemory`.

---

## Providers

Provider packages are very thin adapters that implement `ILLMProvider`. Because the framework handles context reduction, schema generation, and tooling workflows natively, integrating new models takes roughly ~100 lines of code.

### AgentCore.MEAI

Uses the official `Microsoft.Extensions.AI` abstractions. This means AgentCore natively supports **every provider Microsoft supports**, including:
- OpenAI & OpenAI-compatible APIs (LM Studio, Ollama, etc.)
- Azure OpenAI
- Anthropic Claude
- Google Gemini
- Mistral AI
- Local Models (Ollama)
- ...and any other `.AddChatClient()` package.

```csharp
// OpenAI-compatible (LM Studio, Ollama)
.AddOpenAI("qwen-3.54b", "lmstudio", "http://127.0.0.1:1234/v1")

// Azure OpenAI
.AddAzureOpenAI("gpt-4o", endpoint, apiKey)

// Anthropic Claude
.AddAnthropic("claude-3-5-sonnet-20241022", apiKey)

// Ollama
.AddOllama("llama3.1", "http://localhost:11434")

// Custom IChatClient
.WithProvider(new MEAILLMClient(myChatClient))
```

---

## Core Components

### Runtime

| File | Lines | Purpose |
|---|---|---|
| `Agent.cs` | ~325 | `IAgent` interface + `LLMAgent` implementation. Returns `AgentResponse` with text, messages, and token usage. Orchestrates memory recall → executor → memory update. Full agent loop with tool handling. |
| `AgentBuilder.cs` | ~99 | Fluent builder. Wires components, hooks, and options via explicit composition. Builds `LLMAgent`. |
| `AgentResponse.cs` | ~28 | Response record containing sessionId, turn messages, and aggregated token usage. Provides `Text` convenience property. |
| `Runtime/AgentMemory.cs` | ~307 | `IAgentMemory` interface + `FileMemory` + `InMemoryMemory`. Recall/update/clear with JSON file persistence. |

### LLM

| File | Lines | Purpose |
|---|---|---|
| `LLM/LLMExecutor.cs` | ~162 | Consumes raw deltas from provider, emits `TextEvent`/`ToolCallEvent`/`ReasoningEvent`. Handles context reduction, token tracking. Uses Pipeline middleware. |
| `LLM/LLMCall.cs` | ~5 | Simple record: `(IReadOnlyList<Message> Messages, LLMOptions Options)`. No bloat. |
| `LLM/ILLMProvider.cs` | ~14 | Single-method interface: `StreamAsync → IAsyncEnumerable<IContentDelta>`. |
| `LLM/LLMEvent.cs` | ~20 | Events: `TextEvent`, `ReasoningEvent`, `ToolCallEvent`, `LLMMetaEvent`. |
| `LLM/LLMOptions.cs` | ~28 | Flat config class: model, API key, base URL, sampling parameters, response schema, context length, reasoning effort. |
| `LLM/LLMMeta.cs` | ~10 | `FinishReason` enum, `ToolCallMode` enum. |
| `LLM/ContextLengthExceededException.cs` | ~18 | Exception thrown when context exceeds limits. Triggers proactive summarization. |

### Tooling

| File | Lines | Purpose |
|---|---|---|
| `Tooling/Tool.cs` | ~33 | `[Tool]` attribute + `Tool` class (name, description, JSON schema, delegate). |
| `Tooling/ToolRegistry.cs` | ~178 | Registration, lookup, auto-schema generation from method signatures. |
| `Tooling/ToolExecutor.cs` | ~196 | Invocation engine: parameter parsing, validation, CancellationToken injection. Uses Pipeline middleware. |
| `Tooling/ToolOptions.cs` | ~9 | Config: MaxConcurrency, DefaultTimeout. Defaults to framework trimming. |
| `Tooling/ToolRegistryExtensions.cs` | ~71 | `RegisterAll<T>()` — discovers `[Tool]` methods from a type via reflection. |

### Execution (Middleware Pipeline)

| File | Lines | Purpose |
|---|---|---|
| `Execution/Pipeline.cs` | ~28 | Generic middleware pipeline: `PipelineHandler<TRequest, TResult>` and `PipelineMiddleware<TRequest, TResult>`. |

### Tokens

| File | Lines | Purpose |
|---|---|---|
| `Tokens/IContextManager.cs` | ~9 | `IContextManager` interface. |
| `Tokens/SummarizingContextManager.cs` | ~165 | Single implementation: tail-trims to fit context. If provider given, summarizes dropped messages. Handles proactive summarization on context exceeded. |
| `Tokens/TokenManager.cs` | ~43 | `TokenUsage` record with InputTokens, OutputTokens, ReasoningTokens. Cumulative tracking across LLM calls. |
| `Tokens/ITokenCounter.cs` | ~8 | Interface: `CountAsync(messages) → int`. |
| `Tokens/ApproximateTokenCounter.cs` | ~74 | Default `len/4` fallback + Dynamic Response Calibration. |

### Conversation (Internal Primitives)

| File | Lines | Purpose |
|---|---|---|
| `Conversation/Content.cs` | ~45 | `IContent` interface + `Text`, `Reasoning`, `ToolCall`, `ToolResult` records. |
| `Conversation/ContentDelta.cs` | ~17 | `IContentDelta` interface + `TextDelta`, `ToolCallDelta`, `ReasoningDelta`, `MetaDelta` — raw provider streaming types. |
| `Conversation/Message.cs` | ~15 | `Message(Role, IContent)` — the internal message representation. |
| `Conversation/Role.cs` | ~6 | `enum Role { System, Assistant, User, Tool }` |
| `Conversation/MessageKind.cs` | ~12 | `enum MessageKind { Default, Summary, ... }` |
| `Conversation/Extensions.cs` | ~260 | Helpers: `AddUser()`, `AddAssistant()`, `Clone()`, `ToJson()`, serialization for providers. |

### JSON & Schema

| File | Lines | Purpose |
|---|---|---|
| `Json/JsonSchemaExtensions.cs` | ~280 | Generates JSON schemas from .NET types for tool definitions and structured output. |
| `Json/JsonSchemaBuilder.cs` | ~65 | Low-level JSON schema building utilities. |
| `Json/JsonExtensions.cs` | ~22 | JSON serialization helpers. |

### Diagnostics

| File | Lines | Purpose |
|---|---|---|
| `Diagnostics/AgentTelemetryExtensions.cs` | ~50 | OpenTelemetry integration. |
| `Diagnostics/AgentDiagnosticSource.cs` | ~6 | Diagnostic source for activity tracing. |

---

## Project Structure

```
AgentCore/                          # Core framework (~2,100 lines)
├── Agent.cs                        # IAgent, LLMAgent — returns AgentResponse
├── AgentBuilder.cs                 # Fluent builder + explicit composition
├── AgentEvent.cs                   # Base event class + reasoning event
├── AgentResponse.cs                # Response record with text, messages, token usage
├── Runtime/
│   └── AgentMemory.cs              # IAgentMemory, FileMemory, InMemoryMemory
├── LLM/
│   ├── LLMExecutor.cs              # Event-level streaming + middleware
│   ├── LLMCall.cs                  # Simple record: (Messages, Options)
│   ├── ILLMProvider.cs             # Raw provider interface
│   ├── LLMEvent.cs                 # TextEvent, ReasoningEvent, ToolCallEvent, LLMMetaEvent
│   ├── LLMOptions.cs               # Model config
│   ├── LLMMeta.cs                  # FinishReason, ToolCallMode
│   └── ContextLengthExceededException.cs
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
│   ├── TokenManager.cs             # TokenUsage record + cumulative tracking
│   ├── ITokenCounter.cs            # Counter interface
│   └── ApproximateTokenCounter.cs  # Dynamic length approximation
├── Conversation/
│   ├── Content.cs                  # IContent, Text, Reasoning, ToolCall, ToolResult
│   ├── ContentDelta.cs             # Raw streaming delta types
│   ├── Message.cs                  # Message(Role, IContent)
│   ├── Role.cs                     # System, Assistant, User, Tool
│   ├── MessageKind.cs               # Message classification
│   └── Extensions.cs               # Conversation helpers + serialization
├── Json/
│   ├── JsonSchemaExtensions.cs     # Type to JSON Schema generation
│   ├── JsonSchemaBuilder.cs        # Schema building utilities
│   └── JsonExtensions.cs           # JSON helpers
└── Utils/
    └── HelperExtensions.cs         # General utilities

AgentCore.MEAI/                      # MEAI provider package (~500 lines)
├── MEAILLMClient.cs                # ILLMProvider implementation
├── MEAIExtensions.cs               # Message/tool conversion helpers
└── MEAIServiceExtensions.cs        # .AddOpenAI(), .AddAzureOpenAI(), .AddAnthropic(), .AddOllama() extensions
```

---

## Dependencies

The core `AgentCore` package has exactly **2 dependencies**:

- `Microsoft.Extensions.Logging`
- `System.ComponentModel.Annotations`

No DI containers. No bloated abstractions. Just the primitive.
