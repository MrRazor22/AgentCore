# AgentCore 🧠🚀

**A lightweight, high-performance agent orchestration library for .NET.**

AgentCore is a minimalist, dependency-light framework designed for integrating LLMs and tool-calling capability into .NET applications. Rather than hiding execution behind opaque, rigid graph abstractions, AgentCore focuses on **pure architectural boundaries, compiled-expression performance, and developer-controlled pipeline layering.**

It provides the necessary primitives to build robust agents without the boilerplate and dependency churn of enterprise SDKs.

---

## 🛠️ Respect-Worthy Engineering Details

What makes AgentCore distinct from naive wrappers or over-engineered frameworks is how it solves common developer friction points in .NET:

### 1. Compiled Expression Tree Tool Invokers (No Slow Reflection)
Most agent frameworks use slow runtime reflection (`MethodInfo.Invoke`) to execute tools. AgentCore solves this by dynamically compiling C# methods into high-performance delegates at startup using **LINQ Expression Trees** (`DelegateTool.cs`).
* **Type-Safe Parsing**: Automatically converts `JsonObject` arguments into the target C# types.
* **Smart Task Handling**: Seamlessly wraps sync (`void`, `T`), async (`Task`, `Task<T>`), and `CancellationToken`-aware methods under a unified execution interface.
* **Zero Reflection Overhead**: Once built, tool execution runs at near-native compiled speed.

### 2. Pure Pipeline Decorators (Extensibility Without Bloat)
Instead of forcing you into complex middleware configurations or rigid dependency injection configurations, AgentCore uses standard **Decorator Patterns** (Layers). You can intercept, log, modify, or cache LLM requests, tool invocations, or memory updates by stacking decorators:

```csharp
var agent = LLMAgent.Create("my-agent")
    .AddMemoryLayer(memory => new LoggingMemoryDecorator(memory))
    .AddToolingLayer(tooling => new CustomApprovalDecorator(tooling))
    .Build();
```

### 3. Context-Preserving Summarization (`ChatMemoryService`)
Blindly summarizing history leads to context loss, while feeding everything leads to token explosion. The `ChatMemoryService` implements a smart sliding context window:
* **Iterative Compression**: Measures history tokens asynchronously. If they exceed the limit, it compresses only the oldest part of the chat.
* **Raw Context Buffer**: Navigates backwards to guarantee that a configurable buffer of recent raw messages (`MinRecentTokens`, e.g., 2,000 tokens) is never compressed, preserving immediate conversational memory.
* **Summary Isolation**: Prevents summarization-loop feedback by skipping existing system summaries.

---

## 🚀 Quick Start

### 1. Set Up the Agent and LLM Provider
AgentCore integrates natively with `LlmTornado` for streaming and completion.

```csharp
using AgentCore;
using AgentCore.Conversation;
using AgentCore.Providers.Tornado;
using AgentCore.Tooling;
using LlmTornado;

var api = new TornadoApi("YOUR_API_KEY");

var agent = LLMAgent.Create("helper")
    .WithInstructions("You are an assistant with access to system tools.")
    .AddTornado(api, "gpt-4o")
    .WithTools(r => 
    {
        // Dynamically compiles the tool mapping
        r.Register(new DelegateTool(SystemTools.GetStatus));
    })
    .Build();

// Invoke and stream or return the final result
var response = await agent.InvokeAsync(new Text("Check the status of system SR-71."));
Console.WriteLine(response.ForLlm());
```

### 2. Define a C# Method as a Tool
No complex base classes to inherit from. Just write standard C# methods:

```csharp
using AgentCore.Tooling;
using System.ComponentModel;

public class SystemTools
{
    [Tool(description: "Retrieve system status data")]
    public static string GetStatus(
        [Description("Unique system code name")] string systemId)
    {
        return $"System {systemId}: Online, 100% operational.";
    }
}
```

---

## 📂 Codebase Map

* **`Agent.cs` / `AgentBuilder.cs`**: The core execution runner (built on C# 8 `IAsyncEnumerable` streaming) and builder.
* **`Tooling/`**: The runtime compilation engine (`DelegateTool.cs`) and schema generator.
* **`Memory/`**: sliding window token measurement and summarization engine.
* **`Conversation/`**: Polymorphic message payloads (`Text`, `ToolCall`, `ToolResult`, `Reasoning`).
* **`Json/`**: Natively generates JSON Schema models for tools.
