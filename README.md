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

### 2. Create and Run an Agent

```csharp
using AgentCore;
using AgentCore.Context;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Tornado;
using AgentCore.Tool;
using LlmTornado;

// 1. Initialize LLM & Tool primitives
var tornadoApi = new TornadoApi("YOUR_API_KEY");
var llm = new TornadoLLM(tornadoApi, "gpt-4o");
var tools = MethodTool.FromInstance(new SystemTools());

// 2. Assemble the Agent
var agent = new Agent(
    llm: llm,
    toolbox: new Toolbox(tools),
    context: new ChatContext(contextWindow: 128000),
    instructions: [new Text("You are an autonomous engineering assistant.")]
);

// 3. Stream execution events
await foreach (var evt in agent.InvokeStreamingAsync([new Text("Check the status of core-cluster-1")]))
{
    if (evt is Text t)
    {
        Console.Write(t.Value);
    }
}
```

### 3. Adding Middleware Layers

Stack cross-cutting behaviors cleanly without altering core logic:

```csharp
using AgentCore.Layers.Tools;
using AgentCore.Layers.LLM;

// Add human-in-the-loop approval and tool-call detection layers
var toolbox = new ToolboxLayer(new Toolbox(tools))
    .Use(next => new ApprovalLayer(next, autoApprove: false));

var layeredLlm = new LLMLayer(llm)
    .Use(next => new ToolCallDetectionLayer(next));
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
