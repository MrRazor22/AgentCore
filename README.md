# AgentCore - Lightweight C# Framework for creating LLM-Powered Agents

[![NuGet Version](https://img.shields.io/nuget/v/AgentyCore)](https://www.nuget.org/packages/Agenty.Core) [![Build Status](https://img.shields.io/badge/build-passing-brightgreen)](https://github.com/your-org/Agenty/actions) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

AgentCore is a minimal, extensible .NET framework for building single-agent LLM apps. Inspired by the need for simplicity in .NET ecosystems, it focuses on clean tool orchestration without the bloat of full kernels. Perfect for internal tools, prototypes, or production micro-agents where you want full control without ceremony.

Runs on .NET Standard 2.0 (Core/Framework compat), it integrates with OpenAI-compatible LLMs but supports easy one-method provider swaps. No graph workflows, DSLs, or multi-agent complexity—just pure, reliable single-agent flows.

## 🚀 Features
- **Attribute-Driven Tools**: Slap `[Tool]` on methods for auto-registration; generates JSON schemas from params/attrs, parses/validates args on-the-fly, and runs sequential (one per iteration) to dodge LLM crapshoots.
- **Pluggable Executors**: ReAct-style default (`ToolCallingLoop`) with optional sampling presets; extend via `IAgentExecutor` to wire custom agent flow.
- **Token-Smart Streaming**: `ITokenManager` logs in/out deltas per call; stream text/tool chunks via callbacks, with `SlidingWindowTrimmer` pruning history to fit limits (SharpToken backend).
- **Retry Resilience**: `IRetryPolicy` with backoff/jitter and prompt-injected fixes (e.g., "Redo: {error}"); sniffs duplicates/repeats in loops for steady runs.
- **Typed Structured Calls**: `GetStructuredAsync<T>` auto-schemas from .NET types (handles enums/attrs/nullable), validates JSON, retries on busts—POCO outputs without hassle.
- **Scoped DI Core**: `AgentBuilder` bootstraps `IServiceCollection` for per-invoke scopes; inject trimmers/memories/LLMs like it's 2019 ASP.NET—zero globals.
- **File Mem + Hooks**: JSON sessions via `FileMemory` (cached, async); swap impls for `IAgentMemory` (RAG? VectorDB? Yours)—or tool-ify retrieval.

## 📦 Installation
Via NuGet:
```bash
dotnet add package AgentCore
``` 

## ⚡ Quick Start
Build an agent in ~15 lines:

```csharp
using AgentCore;
using AgentCore.BuiltInTools; // Optional: math, search, etc.

var builder = AgentCore.CreateBuilder()
    .AddOpenAI(opts => {
        opts.BaseUrl = "https://your-openai-endpoint/v1"; // Or llama cpp server
        opts.ApiKey = "your-api-key";
        opts.Model = "gpt-4o-mini";
    })
    .AddFileMemory(); // Simple session persistence

var agent = builder.Build("session-1")
    .WithInstructions("You are a helpful assistant.")
    .WithTools<MathTools>() // Register tools via attributes
    .UseExecutor(new ToolCallingLoop(ReasoningMode.Creative, maxIterations: 5)); // Can be provided with custom executor

var response = await agent.InvokeAsync("What's 15% of 250? Explain step-by-step.");
Console.WriteLine(response.Message); // "37.5 - First, convert 15% to decimal..."
```

Streaming? Pass a callback:
```csharp
await agent.InvokeAsync("Fetch weather for Tokyo.", stream: chunk => Console.Write(chunk));
```

## 🛠 Usage

### 1. Building an Agent
Use the fluent `AgentBuilder`:

```csharp
var builder = AgentCore.CreateBuilder()
    .AddOpenAI(opts => opts.ApiKey = "sk-...")
    .AddRetryPolicy(o => o.MaxRetries = 3)
    .AddContextTrimming(o => o.MaxContextTokens = 4096);

var agent = builder.Build("session-1")
    .WithInstructions("You are a pirate. Arrr!")
    .WithTools<WeatherTool>()  // Instance or static class
    .WithTools(myCustomToolInstance);
```

### 2. Invoking
```csharp
var result = await agent.InvokeAsync("Fetch weather for Tokyo.", ct: cts.Token);
if (result.Payload is WeatherData data) { /* Typed! */ }
```

### 3. Custom Tools
```csharp
public class CalcTools
{
    [Tool("multiply", "Multiply two numbers")]
    public int Multiply(int a, int b) => a * b;
}

// Register: agent.WithTools<CalcTools>();
```

### 4. Custom Executor
Just implement the `IAgentExecutor` for your own agent flow.
```csharp
public class MyExecutor : IAgentExecutor {
    public async Task ExecuteAsync(IAgentContext ctx) {
        //In Agnet ctx you get all services required to build your Agnet
    }
}
// agent.UseExecutor(new MyExecutor());
```

### 5. Structured Responses
```csharp
var person = await client.GetStructuredAsync<Person>(
    "Extract name and age from: John is 42.",
    mode: ReasoningMode.Deterministic
);
```

## 🏗 Architecture
```
AgentBuilder → IServiceProvider → Agent
                  ↓
ILLMClient (calls) | IToolRuntime (runs) | IAgentMemory (saves)
                  ↓
IAgentExecutor (loops: reason → tool → repeat)
```

- **IAgentContext**: Scratchpad, streaming, DI scope per invoke.
- **Conversation**: Role-based chat history (System/User/Assistant/Tool).
- **LLMRequest/Response**: Unified for text/tools/structured.

## 🤝 Contributing
Any interest is appreciated, contributions always welcome!

## 📄 License
MIT. See [LICENSE](LICENSE).

