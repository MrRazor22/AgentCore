# AgentCore 🧠⚡

**A primitive-first, minimalist agent framework for .NET.**

AgentCore is built on a mathematical decomposition theorem: **every autonomous agent decomposes into exactly three orthogonal behavioral primitives**, with all extensibility modeled as composable decorator layers over those primitives:

$$\mathcal{A} = (\mathcal{L}, \mathcal{T}, \mathcal{C})$$

* **$\mathcal{L}$ (`ILLM`)**: Generates streaming events from conversation history and tool definitions.
* **$\mathcal{T}$ (`IToolbox`)**: Executes tool calls and returns streaming execution events.
* **$\mathcal{C}$ (`IContext`)**: Real-time Write-Ahead Log (WAL) ingestion and staged message history.

No heavy graph engines, no rigid inheritance hierarchies, and zero accidental complexity.

---

## 🏛️ Architecture & Clean Boundaries

```
                 ┌──────────────────────────┐
                 │       IAgent Loop        │
                 └─────────────┬────────────┘
         ┌─────────────────────┼─────────────────────┐
         ▼                     ▼                     ▼
 ┌───────────────┐     ┌───────────────┐     ┌───────────────┐
 │  ILLM Layer   │     │ Toolbox Layer │     │ Context Layer │
 └───────┬───────┘     └───────┬───────┘     └───────┬───────┘
         ▼                     ▼                     ▼
 ┌───────────────┐     ┌───────────────┐     ┌───────────────┐
 │   Tornado /   │     │  Method Tools │     │  ChatContext  │
 │  Custom LLM   │     │  (Delegates)  │     │ (WAL Stream)  │
 └───────────────┘     └───────────────┘     └───────────────┘
```

1. **Interface-First & Pure Decorators**:
   Extensibility is achieved via clean endomorphic layers (`LLMLayer`, `ToolboxLayer`, `ContextLayer`) without polluting root contracts.
2. **Streaming Context WAL**:
   `ChatContext` ingests events chunk-by-chunk in real-time as they stream from LLMs and tools, ensuring immediate crash resilience and write-ahead durability.
3. **High-Performance Method Tools**:
   Turn any standard C# class method into a tool using `MethodTool.FromInstance()`. Generates native JSON Schema definitions and parses structured arguments automatically.

---

## 🚀 Quick Start

### 1. Define Tools as Plain C# Methods

```csharp
using System.ComponentModel;
using AgentCore.Tool;

public class SystemTools
{
    [Tool("GetSystemStatus", "Retrieves health status of a named system.")]
    public string GetStatus([Description("The system identifier")] string systemId)
    {
        return $"System {systemId}: Healthy, 100% operational.";
    }
}
```

### 2. Assemble and Run the Agent

Build the entire agent in a single, declarative flow:

```csharp
using AgentCore;
using AgentCore.Context;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Tornado;
using AgentCore.Layers.Tools;
using AgentCore.Layers.LLM;
using AgentCore.Tool;

var agent = new Agent(
    llm: llm => llm
        .UseTornado("YOUR_API_KEY", "gpt-4o")
        .UseRetry(maxRetries: 3)
        .UseToolCallDetection(),

    toolbox: tools => tools
        .Configure(parallel: true, maxConcurrency: 8)
        .AddTool(new SystemTools()),

    context: ctx => ctx
        .Configure(contextWindow: 128_000, reserveTokens: 10_000)
        .UseSession(storageDirectory: "./sessions", sessionId: "main-session"),

    instructions: [new Text("You are an autonomous engineering assistant.")]
);

// Stream execution events in real time
await foreach (var evt in agent.InvokeStreamingAsync([new Text("Check the status of core-cluster-1")]))
{
    switch (evt)
    {
        case TextDelta td:       Console.Write(td.Text); break;
        case ReasoningDelta rd: Console.Write($"\n[Thinking: {rd.Thought}]\n"); break;
        case ToolCall tc:       Console.WriteLine($"\n[Tool: {tc.Name}({tc.Arguments})]"); break;
    }
}
```

---

## ⚡ Living Facade & Hot-Reload

### 1. Direct Capability Control
Control underlying providers and layers directly via clean interface extensions:

```csharp
// Change active model on the LLM provider
agent.LLM.SetModel("claude-3-5-sonnet");

// Dynamically attach or detach human-in-the-loop approval gate
agent.Toolbox.UseApproval(async (call, ct) => await AskUserAsync(call));
agent.Toolbox.RemoveApproval();

// Switch active persistence session in place
agent.Context.UseSession("./sessions", "debug-session");
```

### 2. Functional Pipeline Hot-Reload (`With`)
Evolve the pipeline dynamically at runtime without in-flight concurrency hazards:

```csharp
// Evolve toolbox with human approval in one atomic step
agent = agent.With(toolbox: t => t.UseApproval(async (call, ct) => await AskUserAsync(call)));
```

---

## 📂 Repository Structure

* **`AgentCore/`** — The 3 primitives: `Context/`, `LLM/`, `Tool/`, and `Agent.cs`.
* **`AgentCore.Layers/`** — Decorator middleware layers (Approval, ToolCallDetection, ChatPersistence, Retry, MessageMerging).
* **`AgentCore.LLM.Tornado/`** — Tornado provider integration for multi-model LLM streaming.
* **`AgentCore.MCP/`** — Model Context Protocol tools adapter.
* **`AgentCore.Host/`** — Backend host execution engine for Visual Studio Agent.
* **`AgentCore.Tests/`** — Unit, integration, and edge-case test suites organized by the 3 primitives.
* **`AgentCore.Host.Tests/`** — Sandboxed shell and host execution tests.
* **`research/`** — Academic literature reviews, empirical benchmarks across 22 frameworks, and mathematical proofs.
* **`DESIGN.md`** — Core architectural design philosophy and logging principles.
