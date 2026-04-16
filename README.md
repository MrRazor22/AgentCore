# AgentCore

AgentCore is a minimal agent framework for .NET. It acts as a pure execution engine: pass in a task, get back a result. Inside, it manages the full agent loop — context, tool execution, provider coordination — without any of it leaking into your code.

~2,000 lines core. Everything an agent needs. Nothing it doesn't.

---

## Philosophy

Most agent frameworks solve problems they created. They introduce massive state graphs, then need CheckpointSavers. They build complex memory abstractions, then need semantic memory lifecycle hooks. They merge LLM configurations directly into Agent orchestration, leaking "how it thinks" into "what it does."

AgentCore provides a fundamentally different bedrock.

### 1. One content model, strict boundaries
All events in an agent's life — user input, model response, a tool call, a tool result, a tool error, reasoning/thoughts — are treated as the same kind of thing: `IContent`. This means no special error channels or repair hacks. If a tool fails, it's just a message the model reads to self-correct.

Crucially, the layers are strictly separated. The LLM layer handles tokens and network boundaries; it never leaks provider parameters to the Agent layer. The orchestration layer only receives *completed* concerns.

### 2. The loop does exactly one job
The agent loop streams from the LLM, dispatches tool calls, appends results, and checkpoints state. That's all it does. Context reduction and token tracking are handled by dedicated layers the loop simply calls. 

Because the loop inherently pauses at `Task.WhenAll` to await tools, durability is a natural byproduct. Combined with the default `FileMemory`, AgentCore provides perfect, stateless crash recovery per session ID without a massive lifecycle manager or dedicated database thread.

### 3. Direct execution, no magic layers
AgentCore uses direct method calls throughout — no middleware chains, no pipeline abstractions. The LLM executor streams events directly, the tool executor invokes methods directly, and the agent loop orchestrates everything with simple, readable code. This means zero runtime overhead, easy debugging, and full control over execution flow.

### 4. Opinionated Context & Token Calibration
Tail-trimming context windows creates "Amnesia Agents." Exact token counting requires synchronous Tiktoken dependencies that tank performance.

AgentCore avoids this by default:
- It uses a LangChain-inspired `ApproximateTokenCounter` that dynamically calibrates based on actual network responses, remaining incredibly fast while self-correcting drift.
- It defaults to a `SummarizingContextManager` that uses recursive summarization to gracefully fold dropped history into a context scratchpad at the boundary right before the LLM fires, completely decoupled from the true persistent AgentMemory.
- When context limits are exceeded, it proactively triggers summarization and retries the request automatically.

---

## Key Features

- **Zero Middleware Overhead**: Direct method calls throughout the codebase — no pipeline abstractions, no middleware chains. Maximum performance and full control.
- **Streaming-First Architecture**: All operations stream asynchronously — LLM responses, tool execution, context summarization. No blocking, minimal latency.
- **Built-in Tool Approval**: Two-phase approval workflow for sensitive tool execution. Register requests, wait for human decision, execute or reject.
- **AMFS-Style Memory**: Advanced Memory for Semantic Search with confidence decay, inspired by state-of-the-art agent memory systems.
- **Parallel Context Summarization**: When context limits are exceeded, chunks are summarized in parallel for maximum speed.
- **Dynamic Token Calibration**: ApproximateTokenCounter self-corrects based on actual network responses, remaining fast while accurate.
- **Perfect Crash Recovery**: File-based chat persistence enables stateless crash recovery per session ID without lifecycle management.
- **MCP Protocol Support**: Full Model Context Protocol client and server integration for tool interoperability.
- **Code Execution Sandbox**: CodingAgent with Roslyn in-process or isolated process sandbox execution.
- **RAG-Ready Context System**: ContextAssembler composes multiple knowledge sources with token budgets for retrieval-augmented generation.

---

## Packages

| Package | Description |
|---------|-------------|
| `AgentCore` | Core framework — agent loop, tools, memory, diagnostics |
| `AgentCore.CodingAgent` | Code-executing agent with Roslyn/Process sandboxes |
| `AgentCore.Context` | Context assembler + knowledge sources for RAG-style patterns |
| `AgentCore.MCP` | Model Context Protocol client & server integration |
| `AgentCore.MEAI` | Provider package using Microsoft.Extensions.AI |

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
```

### Tool Definition

Mark any method with `[Tool]`. AgentCore uses C# reflection to automatically infer `JsonSchema` parameters and generate JSON-compatible execution delegates.

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

### Tool Approval Workflow

For sensitive operations, use the built-in two-phase approval workflow:

```csharp
var approvalService = new ApprovalService();

var agent = LLMAgent.Create("my-agent")
    .WithInstructions("You are a helpful assistant.")
    .AddOpenAI("gpt-4o", apiKey: "...")
    .WithTools<FilesystemTools>()  // Mark tools with [RequiresApproval]
    .WithApprovalService(approvalService)
    .WithToolOptions(options => options.ApprovalTimeoutSeconds = 300)
    .Build();

// Tools requiring approval will trigger the approval workflow
// You can approve/reject via the ApprovalService from another process/UI
```

### Coding Agent

The `CodingAgent` executes generated C# code to solve tasks. It supports Roslyn-script execution (in-process) or Process sandbox execution (isolated).

```csharp
var agent = CodingAgent.Create("coder")
    .WithInstructions("Solve the user's task by generating and executing C# code.")
    .AddOpenAI("gpt-4o", apiKey: "...")
    .WithMaxSteps(10)
    .WithSandboxPolicy(SandboxPolicy.Restrictive) // or SandboxPolicy.AllowAll
    .Build();

var response = await agent.InvokeAsync("Calculate the first 20 fibonacci numbers and return their sum");
```

Available executors:
- **Roslyn** — runs code in-process via `Microsoft.CodeAnalysis.Scripting`
- **Process** — runs code in an isolated child process

### Context System

The `ContextAssembler` lets you compose multiple knowledge sources with token budgets:

```csharp
var assembler = new ContextAssembler(tokenCounter, logger);

assembler.Register(new FileKnowledge("docs/system.md"), maxTokenBudget: 2000);
assembler.Register(new InMemoryKnowledge("user-prefs", userPrefsContent), maxTokenBudget: 500);

// In your agent's system prompt:
var context = await assembler.AssembleAsync(availableTokens: 6000);
```

### MCP Integration

Connect to external MCP servers and use their tools directly:

```csharp
// Connect to an MCP server
var mcpTools = await McpToolSource.ConnectAsync(
    new StdioClientTransport(new() { Command = "npx", Arguments = ["-y", "@modelcontextprotocol/server-filesystem", "./data"] }),
    "filesystem",
    ct);

var agent = LLMAgent.Create("mcp-agent")
    .WithInstructions("You can use filesystem tools to read and write files.")
    .AddOpenAI("gpt-4o", "...")
    .Build();

// Register MCP tools
mcpTools.RegisterTools(agent.Tools);
```

---

## Architecture

```
┌────────────────────────────────────────────────────────────────┐
│                     Agent Layer (Public API)                    │
│    IAgent: InvokeAsync(string) → string                        │
│            InvokeAsync<T>(string) → T                          │
│            InvokeStreamingAsync(string) → IAsyncEnumerable     │
├────────────────────────────────────────────────────────────────┤
│                    Agent Executor Layer (Control Flow)          │
│    LLMAgent.CoreStreamAsync:                                   │
│      - Handles the full agent loop                             │
│      - Manages tool call iteration                             │
│      - Context length exceeded → proactive summarization       │
├────────────────────────────────────────────────────────────────┤
│                    LLM Executor Layer (Events)                 │
│    ILLMExecutor: StreamAsync → LLMEvent                       │
│    LLM Event: TextEvent, ReasoningEvent, ToolCallEvent        │
│      - Direct streaming, no middleware overhead                 │
├────────────────────────────────────────────────────────────────┤
│                    Tool Executor Layer (Direct)                 │
│    IToolExecutor: HandleToolCallAsync → ToolResult            │
│      - Direct method invocation via reflection                 │
│      - Built-in approval service integration                   │
├────────────────────────────────────────────────────────────────┤
│                    LLM Provider Layer (Raw I/O)                │
│    ILLMProvider: StreamAsync → IAsyncEnumerable<IContentDelta>│
│    Providers: AgentCore.MEAI (OpenAI, Ollama, Anthropic...)    │
└────────────────────────────────────────────────────────────────┘
```

---

## Core Components

### AgentCore (Runtime)

| Component | Description |
|-----------|-------------|
| `Agent.cs` | `IAgent` interface + `LLMAgent` implementation |
| `AgentBuilder.cs` | Fluent builder for agent composition |
| `Memory/CoreMemory.cs` | Core memory with scratchpad tools |
| `Tooling/` | `[Tool]` attribute, `ToolRegistry`, `ToolExecutor`, ApprovalService |
| `LLM/` | `ILLMExecutor`, `ILLMProvider`, LLM events streaming |
| `Tokens/` | Token counting, context management, summarization |
| `Conversation/` | `IContent`, `Message`, `Role` primitives, chat persistence |
| `Json/` | JSON Schema generation for tools/structured output |
| `Diagnostics/` | Diagnostic source for observability |

### AgentCore.CodingAgent

| Component | Description |
|-----------|-------------|
| `CodingAgent.cs` | Code-executing agent implementation |
| `CodingAgentBuilder.cs` | Fluent builder with sandbox config |
| `RoslynScriptExecutor.cs` | In-process C# execution |
| `ProcessExecutor.cs` | Isolated process execution |
| `ToolBridge.cs` | Bridges agent tools to code execution |

### AgentCore.Context

| Component | Description |
|-----------|-------------|
| `ContextAssembler.cs` | Composes context sources with token budgets |
| `FileKnowledge.cs` | Persistent file-based knowledge source |
| `InMemoryKnowledge.cs` | In-memory knowledge source |

### AgentCore.MCP

| Component | Description |
|-----------|-------------|
| `Client/McpToolSource.cs` | Connect to MCP servers |
| `Server/AgentMcpServer.cs` | Expose agent tools via MCP |

---

## Providers (AgentCore.MEAI)

Uses the official `Microsoft.Extensions.AI` abstractions. Supports every provider Microsoft supports:

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

## Dependencies

The core `AgentCore` package has exactly **2 dependencies**:

- `Microsoft.Extensions.Logging`
- `System.ComponentModel.Annotations`

No DI containers. No bloated abstractions. Just the primitives you need.

---

## Project Structure

```
AgentCore/                          # Core framework (~2,000 lines)
├── Agent.cs                        # IAgent, LLMAgent
├── AgentBuilder.cs                 # Fluent builder
├── Memory/
│   ├── CoreMemory.cs              # Core memory with scratchpad tools
│   ├── MemoryEngine.cs            # AMFS-style memory with embeddings
│   └── ScratchpadTools.cs         # Built-in scratchpad tools
├── LLM/
│   ├── LLMExecutor.cs              # Direct event streaming
│   ├── ILLMProvider.cs            # Raw provider interface
│   ├── LLMOptions.cs               # Model config
│   └── LLMEvent.cs                 # TextEvent, ReasoningEvent, ToolCallEvent
├── Tooling/
│   ├── Tool.cs                     # [Tool] attribute
│   ├── ToolRegistry.cs            # Registration + auto-schema
│   ├── ToolExecutor.cs             # Direct invocation with approval
│   ├── ApprovalService.cs          # Two-phase approval workflow
│   └── ToolRegistryExtensions.cs # RegisterAll<T>() reflection
├── Tokens/
│   ├── IContextCompactor.cs       # Context reduction interface
│   ├── SummarizingContextCompactor.cs # Parallel summarization
│   ├── TokenManager.cs             # TokenUsage tracking
│   └── ApproximateTokenCounter.cs # Dynamic calibration
├── Conversation/
│   ├── Content.cs                  # IContent primitives
│   ├── Message.cs                  # Message(Role, IContent)
│   ├── Chat.cs                     # IChat, InMemoryChat, ChatFileStore
│   └── Extensions.cs              # Helpers + serialization
├── Json/
│   ├── JsonSchemaExtensions.cs   # Type to JSON Schema
│   └── JsonExtensions.cs          # JSON serialization helpers
└── Diagnostics/
    └── AgentDiagnosticSource.cs   # Diagnostic source

AgentCore.CodingAgent/              # Code-executing agent
├── CodingAgent.cs                  # Agent implementation
├── CodingAgentBuilder.cs           # Builder with sandbox config
├── RoslynScriptExecutor.cs        # In-process execution
├── ProcessExecutor.cs             # Process sandbox
└── ToolBridge.cs                  # Tool bridging

AgentCore.Context/                  # Context/knowledge system
├── ContextAssembler.cs            # Compose sources with budgets
├── FileKnowledge.cs               # File-based knowledge
└── InMemoryKnowledge.cs           # In-memory knowledge

AgentCore.MCP/                      # MCP integration
├── Client/
│   └── McpToolSource.cs           # Connect to MCP servers
└── Server/
    └── AgentMcpServer.cs          # Expose tools via MCP

AgentCore.MEAI/                     # MEAI provider package
├── MEAILLMClient.cs                # ILLMProvider implementation
└── MEAIExtensions.cs              # .AddOpenAI(), .AddAnthropic() extensions
```
