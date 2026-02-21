# AgentCore Codebase Complete Reference

This document provides a comprehensive, line-by-line understanding of the entire AgentCore framework. Reading this gives you insight into every significant piece of code in the codebase.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Module Map](#module-map)
3. [Core Components](#core-components)
4. [Tools Module](#agentcoretools)
5. [Runtime Module](#agentcoreruntime)
6. [LLM Module](#agentcorellm)
7. [Tokens Module](#agentcoretokens)
8. [Chat Module](#agentcorechat)
9. [JSON Module](#agentcorejson)
10. [Providers Module](#agentcoreproviders)
11. [Utils Module](#agentcoreutils)
12. [Component Interactions](#component-interactions)
13. [Execution Flows](#execution-flows)
14. [TestApp Reference](#testapp-reference)

---

## Architecture Overview

AgentCore is a **modular LLM Agent Framework** built on .NET that enables building AI agents with tool-calling capabilities. The architecture follows several key design patterns:

### Design Patterns Used

1. **Builder Pattern** (`AgentBuilder`) - Fluent API for constructing agents with configuration
2. **Dependency Injection** - Full Microsoft.Extensions.DependencyInjection integration
3. **Pipeline/Chain of Responsibility** - Handler chain for processing LLM stream chunks
4. **Template Method** - Extensible protocol classes for LLM requests/responses
5. **Strategy Pattern** - Pluggable LLM providers (OpenAI, extensible to others)
6. **Registry Pattern** - Tool registration and catalog management

### Architecture Layers

```
┌─────────────────────────────────────────────────────────────┐
│                    Agent (Runtime Layer)                   │
│         LLMAgent → AgentExecutor → ToolCallingLoop         │
├─────────────────────────────────────────────────────────────┤
│                   LLM Execution Layer                       │
│     LLMExecutor → Handlers → ILLMStreamProvider           │
├─────────────────────────────────────────────────────────────┤
│                      Protocol Layer                          │
│          LLMRequest / LLMResponse / LLMStreamChunk          │
├─────────────────────────────────────────────────────────────┤
│                   Supporting Services                       │
│     Tools │ Tokens │ Chat │ Json │ Providers │ Utils       │
└─────────────────────────────────────────────────────────────┘
```

---

## Module Map

```
AgentCore/
├── Tools/
│   ├── Tool.cs                 # Tool attribute and class definition
│   ├── ToolRegistry.cs         # Tool registration and catalog interfaces
│   ├── ToolRuntime.cs          # Tool execution engine
│   └── ToolCallParser.cs       # Tool call parsing and validation
├── Runtime/
│   ├── Agent.cs                # Main agent abstraction
│   ├── AgentBuilder.cs         # Fluent builder for agents
│   ├── AgentExecutor.cs        # Agent loop execution
│   └── AgentMemory.cs          # Persistent conversation memory
├── LLM/
│   ├── Protocol/
│   │   ├── LLMRequest.cs       # Request protocol
│   │   ├── LLMResponse.cs      # Response protocol
│   │   └── LLMStreamChunk.cs   # Streaming protocol
│   ├── Handlers/
│   │   ├── IChunkHandler.cs    # Handler interface
│   │   ├── TextHandler.cs      # Text accumulation
│   │   ├── ToolCallHandler.cs  # Tool call parsing
│   │   ├── StructuredHandler.cs# JSON structured output
│   │   ├── TokenUsageHandler.cs# Token usage tracking
│   │   └── FinishHandler.cs    # Finish reason capture
│   └── Execution/
│       ├── LLMExecutor.cs      # LLM call orchestration
│       └── RetryPolicy.cs      # Retry logic
├── Tokens/
│   ├── TokenManager.cs         # Token tracking
│   ├── TikTokenCounter.cs      # Token counting with SharpToken
│   └── ContextManager.cs       # Context window management
├── Chat/
│   ├── Conversation.cs        # Message list container
│   ├── ChatContent.cs         # Content types (text, tool call, result)
│   └── ConversationExtensions.cs# Helper methods
├── Json/
│   ├── JsonSchemaBuilder.cs   # Fluent schema builder
│   ├── JsonSchemaExtensions.cs# Schema generation and validation
│   └── JsonExtensions.cs      # JSON utilities
├── Providers/
│   └── ILLMStreamProvider.cs  # Provider interface
│   └── OpenAI/
│       ├── OpenAILLMClient.cs # OpenAI implementation
│       └── OpenAIExtensions.cs# OpenAI helpers
└── Utils/
    └── HelperExtensions.cs    # String helpers

TestApp/
├── Program.cs                  # Entry point
├── ChatBotAgent.cs             # Sample agent implementation
└── BuiltInTools/
    ├── MathTools.cs           # Math operations
    ├── WeatherTools.cs        # Weather API simulation
    ├── SearchTools.cs         # Search operations
    ├── RAGTools.cs            # RAG operations
    ├── GeoTools.cs            # Geographic operations
    └── ConversionTools.cs     # Unit conversions
```

---

## Core Components

### AgentCore/Tools/Tool.cs

This file defines the tool abstraction using attributes.

**Key Elements:**

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class ToolAttribute : Attribute
```

- `ToolAttribute` is applied to methods to mark them as callable tools
- Parameters: `Name` (optional tool name), `Description` (what the tool does)
- When a method is decorated with `[Tool]`, the framework automatically generates a JSON schema

```csharp
public class Tool
```

- Represents a callable tool with:
  - `Name` - Unique identifier
  - `Description` - Human-readable description
  - `Parameters` - JSON Schema for arguments
  - `Delegate` - The actual method to invoke
  - `IsAsync` - Whether the method is async

**Location:** `AgentCore/Tools/Tool.cs`

---

### AgentCore/Tools/ToolRegistry.cs

Manages tool registration and lookup.

**Key Interfaces:**

```csharp
public interface IToolRegistry
```

- `RegisterTool(Tool tool)` - Register a single tool
- `RegisterTools(IEnumerable<Tool> tools)` - Register multiple tools
- `RegisterFromType(Type type)` - Register all `[Tool]` decorated methods from a type
- `RegisterFromInstance(object instance)` - Register instance methods

```csharp
public interface IToolCatalog
```

- `GetTool(string name)` - Retrieve a tool by name
- `GetAllTools()` - Get all registered tools
- `ToolExists(string name)` - Check if tool exists

**Implementation:**

```csharp
public class ToolRegistryCatalog : IToolRegistry, IToolCatalog
```

- Implements both interfaces (registry + catalog)
- Uses `Dictionary<string, Tool>` for storage
- `ToolSchemaBuilder` generates JSON schemas from method signatures
- Handles parameter parsing, nullable types, and default values

**Location:** `AgentCore/Tools/ToolRegistry.cs`

---

### AgentCore/Tools/ToolRuntime.cs

Executes tools with timeout support and cancellation token injection.

**Key Interface:**

```csharp
public interface IToolRuntime
```

- `HandleToolCallAsync(ToolCall toolCall, CancellationToken cancellationToken)` - Execute a tool call

**Implementation:**

```csharp
public class ToolRuntime : IToolRuntime
```

**Execution Process:**

1. **Lookup** - Finds tool in catalog by name
2. **Parse Arguments** - Converts JSON arguments to method parameters using `JsonSerializer.Deserialize`
3. **CancellationToken Injection** - Automatically injects `CancellationToken` as last parameter if present
4. **Invocation** - Calls `tool.Delegate.DynamicInvoke(args)`
5. **Await Task** - If result is `Task`, awaits it to get actual result
6. **Error Handling** - Wraps errors in `ToolExecutionException` with tool name

**Exception:**

```csharp
public class ToolExecutionException : Exception
```

- Contains `ToolName` property
- Wraps any exception that occurs during tool execution

**Location:** `AgentCore/Tools/ToolRuntime.cs`

---

### AgentCore/Tools/ToolCallParser.cs

Parses and validates tool calls from LLM responses.

**Key Interface:**

```csharp
public interface IToolCallParser
```

- `ParseToolCalls(string text)` - Extract tool calls from text response
- `ParseToolCalls(JObject? structured)` - Extract from structured JSON
- `Validate(ToolCall toolCall)` - Validate tool call against schema

**Implementation:**

```csharp
public class ToolCallParser : IToolCallParser
```

**Parsing Strategies:**

1. **Text Parsing** - Uses regex to find patterns like `tool_name(arg1=value1)`
2. **Structured Parsing** - Directly deserializes from JSON

**Validation:**

1. Checks tool exists in catalog
2. Validates arguments against JSON schema
3. Returns `ToolValidationException` with detailed errors

**Exception:**

```csharp
public class ToolValidationException : Exception
```

- `Errors` property - List of validation failure messages

**Location:** `AgentCore/Tools/ToolCallParser.cs`

---

## AgentCore/Runtime

### AgentCore/Runtime/Agent.cs

The main agent abstraction - this is where everything comes together.

**Key Interfaces:**

```csharp
public interface IAgent
```

- `InvokeAsync(string input, ...)` - Main entry point for agent invocation
- Returns `Task<AgentResponse>`

```csharp
public interface IAgentContext
```

- `Input` - User input
- `Services` - IServiceProvider for DI
- `ScratchPad` - Conversation for this invocation
- `CancellationToken` - Cancellation signal
- `StreamCallback` - Optional streaming callback
- `SessionId` - Session identifier

```csharp
public class AgentResponse
```

- `Message` - Text response
- `Payload` - Optional structured output
- `ToolCalls` - Any tool calls made
- `TokenUsage` - Token consumption data

**Implementation:**

```csharp
public class LLMAgent : IAgent
```

**Construction (via AgentBuilder):**

1. Receives `AgentConfig` with instructions, model, tools
2. Gets `IServiceProvider` from AgentBuilder
3. On `InvokeAsync`:
   - Creates new DI scope
   - Generates session ID (GUID)
   - Creates `AgentContext` with services and fresh `ScratchPad`
   - Adds system prompt to scratch pad
   - Gets `IAgentExecutor` from DI
   - Executes the executor
   - Returns `AgentResponse`

**System Prompt Handling:**

- If instructions provided, wraps as system message
- Appends tool definitions to system prompt

**Location:** `AgentCore/Runtime/Agent.cs`

---

### AgentCore/Runtime/AgentBuilder.cs

Fluent builder for constructing agents with dependency injection.

**Key Class:**

```csharp
public class AgentBuilder
```

**Configuration Properties:**

- `Instruction` - System prompt/instructions
- `Model` - LLM model identifier
- `MaxIterations` - Maximum tool call iterations (default: 10)
- `Tools` - List of types to register
- `OutputType` - Optional structured output type
- `Services` - IServiceCollection for custom services

**Builder Methods:**

```csharp
public AgentBuilder WithInstructions(string instructions)
public AgentBuilder WithModel(string model)
public AgentBuilder WithMaxIterations(int max)
public AgentBuilder WithTools<T>()
public AgentBuilder WithTools(object instance)
public AgentBuilder WithOutput<T>()
public AgentBuilder ConfigureServices(Action<IServiceCollection> configure)
```

**Build Process:**

```csharp
public LLMAgent Build(string? sessionId = null)
```

1. Creates new `ServiceCollection`
2. Adds core services:
   - `IToolRegistry` → `ToolRegistryCatalog`
   - `IToolRuntime` → `ToolRuntime`
   - `IToolCallParser` → `ToolCallParser`
   - `ITokenManager` → `TokenManager`
   - `IContextManager` → `ContextManager`
   - `ILLMExecutor` → `LLMExecutor`
   - `IAgentMemory` → `FileMemory` (if added)
   - All registered chunk handlers
   - LLM provider (OpenAI by default)
3. Adds user-configured services
4. Builds `IServiceProvider`
5. Creates `AgentConfig`
6. Returns `LLMAgent`

**Extension Methods:**

```csharp
public static class AgentBuilderExtensions
```

- `AddOpenAI(Action<OpenAIOptions> configure)` - Configure OpenAI provider
- `AddRetryPolicy(Action<RetryPolicyOptions> configure)` - Configure retry behavior
- `AddContextTrimming(Action<ContextTrimmingOptions> configure)` - Configure token limits
- `AddFileMemory(string? basePath)` - Add file-based memory

**Location:** `AgentCore/Runtime/AgentBuilder.cs`

---

### AgentCore/Runtime/AgentExecutor.cs

Implements the agent loop execution pattern.

**Key Interface:**

```csharp
public interface IAgentExecutor
```

- `ExecuteAsync(IAgentContext context)` - Execute the agent loop

**Implementation:**

```csharp
public class ToolCallingLoop : IAgentExecutor
```

**Properties:**

- `ReasoningMode` - `Deterministic` or `Creative` (affects temperature)
- `MaxIterations` - Maximum loop iterations
- `MaxTokens` - Maximum tokens in response

**Execution Loop:**

```csharp
public async Task ExecuteAsync(IAgentContext context)
```

```
for (iteration = 0; iteration < MaxIterations; iteration++)
{
    1. Add user message to ScratchPad
    2. Get ILLMExecutor from context.Services
    3. Get IToolCatalog from context.Services
    4. Build LLMRequest:
       - Messages from ScratchPad
       - Tools from catalog
       - Generation options (temperature, etc.)
    5. Call executor.ExecuteAsync(request, handlers, cancellationToken)
    6. Get response
    7. If response has ToolCalls:
       a. For each tool call:
          - Parse and validate
          - Execute via IToolRuntime
          - Add ToolCallResult to ScratchPad
       b. Continue to next iteration
    8. If no tool call:
       a. Add assistant message to ScratchPad
       b. Return AgentResponse
}
9. If max iterations reached, throw or return partial
```

**Finish Conditions:**

- Text response received (no tool call)
- Structured output received
- Error/exception
- Max iterations exhausted

**Location:** `AgentCore/Runtime/AgentExecutor.cs`

---

### AgentCore/Runtime/AgentMemory.cs

Provides persistent conversation storage.

**Key Interface:**

```csharp
public interface IAgentMemory
```

- `LoadAsync(string sessionId)` - Load conversation from storage
- `SaveAsync(string sessionId, Conversation conversation)` - Save conversation

**Implementation:**

```csharp
public class FileMemory : IAgentMemory
```

**Storage Format:**

- JSON files in `sessions/` directory (configurable)
- Filename: `{sessionId}.json`

**Features:**

- Async file I/O
- JSON serialization/deserialization of `Conversation`
- Caching: keeps loaded conversations in memory dictionary
- Thread-safe with `ConcurrentDictionary`

**Usage:**

```csharp
builder.AddFileMemory("./data/sessions");
var agent = builder.Build("session-1"); // Loads or creates
```

**Location:** `AgentCore/Runtime/AgentMemory.cs`

---

## AgentCore/LLM

### Protocol Layer

#### AgentCore/LLM/Protocol/LLMRequest.cs

Represents a stable request model for LLM calls.

**Key Classes:**

```csharp
public class LLMRequest
```

- `Messages` - List of chat messages
- `Tools` - Optional tool definitions (JSON schema array)
- `ToolChoice` - `none`, `auto`, or specific tool
- `Temperature` - Sampling temperature (0.0-2.0)
- `TopP` - Nucleus sampling
- `MaxTokens` - Maximum tokens to generate
- `Stream` - Whether to stream response
- `ResponseFormat` - `text` or `json_object`
- `Seed` - For deterministic sampling
- `Stop` - Stop sequences

```csharp
public class LLMGenerationOptions
```

- Fluent builder for LLM options
- Methods: `WithTemperature()`, `WithMaxTokens()`, etc.

```csharp
public enum ToolCallMode
```

- `None` - Disable tool calls
- `Auto` - Let model decide
- `Required` - Force tool call

**Location:** `AgentCore/LLM/Protocol/LLMRequest.cs`

---

#### AgentCore/LLM/Protocol/LLMResponse.cs

Represents the LLM response.

**Key Classes:**

```csharp
public class LLMResponse
```

- `Text` - Text content
- `ToolCalls` - List of tool call requests
- `Output` - Structured output (if using JSON mode)
- `FinishReason` - Why generation stopped
- `TokenUsage` - Input/output token counts

```csharp
public class TokenUsage
```

- `PromptTokens` - Tokens in request
- `CompletionTokens` - Tokens in response
- `TotalTokens` - Sum of both
- `CachedTokens` - Cached tokens (if supported)

**Finish Reasons:**

- `stop` - Natural stop
- `length` - Max tokens reached
- `tool_calls` - Tool invocation triggered
- `content_filter` - Content filtered
- `error` - Error occurred

**Location:** `AgentCore/LLM/Protocol/LLMResponse.cs`

---

#### AgentCore/LLM/Protocol/LLMStreamChunk.cs

Unified streaming unit for real-time LLM responses.

**Key Class:**

```csharp
public class LLMStreamChunk
```

**Properties:**

- `Kind` - Type of chunk
- `Content` - The actual content
- `Index` - Chunk sequence number

```csharp
public enum StreamKind
```

- `Text` - Text delta
- `ToolCallDelta` - Tool call argument delta
- `Structured` - JSON object chunk
- `Usage` - Token usage update
- `Finish` - Final chunk with finish reason

**Streaming Flow:**

1. LLM sends chunks in real-time
2. Each chunk has a `Kind` indicating what type of content
3. Handlers process chunks based on `Kind`

**Location:** `AgentCore/LLM/Protocol/LLMStreamChunk.cs`

---

### Handlers Layer

#### AgentCore/LLM/Handlers/IChunkHandler.cs

Base interface for all stream chunk handlers.

**Key Interface:**

```csharp
public interface IChunkHandler
```

- `OnRequest(LLMRequest request)` - Called before sending request
- `OnChunk(LLMStreamChunk chunk)` - Called for each streaming chunk
- `OnResponse(LLMResponse response)` - Called after all chunks received

**Handler Order (Pipeline):**

1. TextHandler - Accumulates text
2. ToolCallHandler - Parses tool calls
3. StructuredHandler - Parses JSON
4. TokenUsageHandler - Tracks usage
5. FinishHandler - Captures finish reason

**Location:** `AgentCore/LLM/Handlers/IChunkHandler.cs`

---

#### AgentCore/LLM/Handlers/TextHandler.cs

Accumulates text deltas and detects inline tool calls.

**Key Class:**

```csharp
public class TextHandler : IChunkHandler
```

**Behavior:**

1. In `OnChunk`: Appends `chunk.Content` to `currentText`
2. In `OnResponse`: 
   - Checks if response has tool calls (from other handlers)
   - Sets `response.Text` to accumulated text
   - Detects inline tool calls via regex if no structured tool calls

**Inline Tool Call Detection:**

```csharp
// Pattern: tool_name(arg1=value1, arg2=value2)
var match = Regex.Match(text, @"(\w+)\(([\w\W]*?)\)"");
```

**Location:** `AgentCore/LLM/Handlers/TextHandler.cs`

---

#### AgentCore/LLM/Handlers/ToolCallHandler.cs

Parses streaming tool call deltas into complete tool calls.

**Key Class:**

```csharp
public class ToolCallHandler : IChunkHandler
```

**Behavior:**

1. In `OnChunk` with `StreamKind.ToolCallDelta`:
   - Extracts tool call index and function name
   - Appends arguments to `ToolCall.Arguments` string builder
2. In `OnResponse`:
   - Parses accumulated arguments as JSON
   - Creates `ToolCall` objects
   - Adds to `response.ToolCalls`

**Streaming Tool Call Format (OpenAI):**

```json
{
  "index": 0,
  "id": "call_abc123",
  "type": "function",
  "function": {
    "name": "get_weather",
    "arguments": "{\"location\": \"Tokyo\"}"
  }
}
```

**Location:** `AgentCore/LLM/Handlers/ToolCallHandler.cs`

---

#### AgentCore/LLM/Handlers/StructuredHandler.cs

Handles JSON structured output mode.

**Key Class:**

```csharp
public class StructuredHandler : IChunkHandler
```

**Behavior:**

1. In `OnRequest`: Sets `response_format: { type: "json_object" }`
2. In `OnChunk` with `StreamKind.Structured`:
   - Accumulates JSON content
   - Optionally validates against schema
3. In `OnResponse`:
   - Parses accumulated JSON
   - Deserializes to `response.Output` (of type T)

**Schema Validation:**

```csharp
public class StructuredHandler<T> : IChunkHandler
```

- Uses `JsonSchemaExtensions.Validate()` for validation
- Throws if JSON doesn't match schema

**Location:** `AgentCore/LLM/Handlers/StructuredHandler.cs`

---

#### AgentCore/LLM/Handlers/TokenUsageHandler.cs

Records and resolves token usage from streaming chunks.

**Key Class:**

```csharp
public class TokenUsageHandler : IChunkHandler
```

**Behavior:**

1. In `OnChunk` with `StreamKind.Usage`:
   - Updates `currentUsage` from chunk content
2. In `OnResponse`:
   - Sets `response.TokenUsage`
   - If no usage from stream, approximates using `ITokenCounter`

**Approximation Logic:**

- Counts tokens in prompt text
- Counts tokens in completion text
- Falls back to character-based estimate if counter unavailable

**Location:** `AgentCore/LLM/Handlers/TokenUsageHandler.cs`

---

#### AgentCore/LLM/Handlers/FinishHandler.cs

Captures the finish reason from the final chunk.

**Key Class:**

```csharp
public class FinishHandler : IChunkHandler
```

**Behavior:**

1. In `OnChunk` with `StreamKind.Finish`:
   - Stores `finishReason` and `logprobs` if present
2. In `OnResponse`:
   - Sets `response.FinishReason`

**Location:** `AgentCore/LLM/Handlers/FinishHandler.cs`

---

### Execution Layer

#### AgentCore/LLM/Execution/LLMExecutor.cs

Orchestrates LLM calls with context trimming, handlers, and retry logic.

**Key Interface:**

```csharp
public interface ILLMExecutor
```

- `ExecuteAsync(LLMRequest request, IEnumerable<IChunkHandler> handlers, CancellationToken ct)`
- Returns `Task<LLMResponse>`

**Implementation:**

```csharp
public class LLMExecutor : ILLMExecutor
```

**Execution Flow:**

```
1. Get ILLMStreamProvider from DI
2. Get IContextManager from DI
3. Get ITokenManager from DI
4. 
5. Prepare Request:
   a. contextManager.Trim(messages, options)  // Fit in context window
   b. request.Messages = trimmedMessages
   c. Calculate estimated tokens
   d. Call handlers.OnRequest(request)
6. 
7. Execute Request:
   a. Call provider.StreamAsync(request, onChunk, ct)
   b. For each chunk:
      - Determine chunk.Kind
      - Call matching handler.OnChunk(chunk)
      - Or call all handlers if Kind is unknown
8. 
9. Finalize:
   a. Call handlers.OnResponse(response)
   b. tokenManager.Record(response.TokenUsage)
10. Return response
```

**Error Handling:**

- Wraps provider errors
- Handles timeout exceptions

**Location:** `AgentCore/LLM/Execution/LLMExecutor.cs`

---

#### AgentCore/LLM/Execution/RetryPolicy.cs

Implements retry logic with backoff and jitter.

**Key Interfaces:**

```csharp
public interface IRetryPolicy
```

- `ShouldRetry(Exception ex, int attempt)` - Determine if should retry
- `GetRetryDelay(int attempt)` - Calculate delay before next retry

**Implementation:**

```csharp
public class RetryPolicy : IRetryPolicy
```

**Configuration:**

```csharp
public class RetryPolicyOptions
```

- `MaxRetries` - Maximum retry attempts (default: 3)
- `InitialDelayMs` - Starting delay (default: 1000ms)
- `MaxDelayMs` - Maximum delay cap (default: 30000ms)
- `Multiplier` - Exponential backoff (default: 2.0)
- `Jitter` - Random jitter factor (default: 0.1)
- `RetryOn` - Exception types to retry on

**Retry Strategy:**

```
delay = min(InitialDelay * (Multiplier ^ attempt), MaxDelayMs)
delay += random(-Jitter * delay, Jitter * delay)
```

**Exceptions:**

```csharp
public class RetryException : Exception
```

- Contains `Attempt` count
- Wraps underlying exception

```csharp
public class EarlyStopException : Exception
```

- Used to stop retry loop (e.g., after max attempts)

**Location:** `AgentCore/LLM/Execution/RetryPolicy.cs`

---

## AgentCore/Tokens

### AgentCore/Tokens/TokenManager.cs

Tracks cumulative token usage across calls.

**Key Interface:**

```csharp
public interface ITokenManager
```

- `CurrentUsage` - Cumulative tokens used
- `Record(TokenUsage usage)` - Add to cumulative count
- `Reset()` - Clear accumulated usage
- `GetCostEstimate()` - Estimate cost based on model pricing

**Implementation:**

```csharp
public class TokenManager : ITokenManager
```

**Features:**

- Thread-safe with `Interlocked`
- Maintains running totals:
  - `TotalPromptTokens`
  - `TotalCompletionTokens`
  - `TotalTokens`
- If provider doesn't report usage, approximates using `ITokenCounter`

**Location:** `AgentCore/Tokens/TokenManager.cs`

---

### AgentCore/Tokens/TikTokenCounter.cs

Token counting using SharpToken library.

**Key Interface:**

```csharp
public interface ITokenCounter
```

- `Count(string text)` - Count tokens in text
- `Count(Messages messages)` - Count tokens in messages

**Implementation:**

```csharp
public class TikTokenCounter : ITokenCounter
```

**Encoding Support:**

- `cl100k_base` - OpenAI's encoding (GPT-4, GPT-3.5)
- `o200k_base` - OpenAI's newer encoding (GPT-4o)

**Tokenization:**

1. Encodes text using SharpToken
2. Splits by encoding type (chat vs. raw text)
3. Adds overhead tokens for message format

**Message Token Formula (OpenAI):**

```
per-message: 4 tokens (always)
per-message: +content tokens
+ "name" field: +1 token
+ "role" field: +1 token
+ "tool" role: +3 tokens
```

**Location:** `AgentCore/Tokens/TikTokenCounter.cs`

---

### AgentCore/Tokens/ContextManager.cs

Manages context window by trimming messages to fit token limits.

**Key Interface:**

```csharp
public interface IContextManager
```

- `Trim(List<ChatMessage> messages, LLMGenerationOptions options)` - Trim messages to fit

**Implementation:**

```csharp
public class ContextManager : IContextManager
```

**Configuration:**

```csharp
public class ContextTrimmingOptions
```

- `MaxContextTokens` - Maximum tokens in context (default: 4096)
- `ReservedTokens` - Tokens reserved for completion (default: 512)
- `Strategy` - `SlidingWindow` or `Summarize`

**Trimming Strategy (SlidingWindow):**

```
1. Calculate available tokens = MaxContextTokens - ReservedTokens
2. If current tokens <= available: return as-is
3. Preserve:
   a. System message (always keep)
   b. Last tool call (if any)
   c. Last N user messages
4. From oldest messages, remove until fit
5. Add "[Previous conversation trimmed]" marker
```

**Smart Preservation:**

- System message is always kept
- Last tool call is preserved (important for continuity)
- Recent messages prioritized over older ones

**Location:** `AgentCore/Tokens/ContextManager.cs`

---

## AgentCore/Chat

### AgentCore/Chat/Conversation.cs

Message list container with role-based messages.

**Key Class:**

```csharp
public class Conversation
```

**Properties:**

- `Messages` - `List<ChatMessage>`
- `SystemPrompt` - Optional persistent system message
- `SessionId` - Session identifier
- `CreatedAt` / `UpdatedAt` - Timestamps

**Message Structure:**

```csharp
public record ChatMessage
```

- `Role` - `system`, `user`, `assistant`, `tool`
- `Content` - Message content (string or nested content)
- `Name` - Optional name for user messages
- `ToolCallId` - For tool results (correlates to assistant tool call)
- `ToolCall` - For assistant tool call requests

**Location:** `AgentCore/Chat/Conversation.cs`

---

### AgentCore/Chat/ChatContent.cs

Defines different content types in conversations.

**Key Interfaces and Classes:**

```csharp
public interface IChatContent
```

```csharp
public class TextContent : IChatContent
```

- Simple text content

```csharp
public class ToolCall : IChatContent
```

- `Id` - Unique tool call identifier
- `Name` - Tool name being called
- `Arguments` - Arguments as JSON string

```csharp
public class ToolCallResult : IChatContent
```

- `ToolCallId` - Correlates to the tool call
- `Content` - Result content (string or error)
- `IsError` - Whether the tool execution failed

**Role Enum:**

```csharp
public enum Role
```

- `System` - System instructions
- `User` - User input
- `Assistant` - LLM responses
- `Tool` - Tool execution results

**Location:** `AgentCore/Chat/ChatContent.cs`

---

### AgentCore/Chat/ConversationExtensions.cs

Helper methods for working with conversations.

**Key Extension Methods:**

```csharp
public static class ConversationExtensions
```

- `AddSystemMessage(this Conversation, string content)` - Add system message
- `AddUserMessage(this Conversation, string content)` - Add user message
- `AddAssistantMessage(this Conversation, string content, ...)` - Add assistant message
- `AddToolResult(this Conversation, string toolCallId, string result)` - Add tool result
- `GetLastUserMessage(this Conversation)` - Get most recent user message
- `GetAllToolCalls(this Conversation)` - Extract all tool calls
- `Clone(this Conversation)` - Deep clone conversation
- `ToJson(this Conversation)` - Serialize to JSON
- `FromJson(string json)` - Deserialize from JSON
- `ToOpenAIMessages(this Conversation)` - Convert to OpenAI format

**Location:** `AgentCore/Chat/ConversationExtensions.cs`

---

## AgentCore/Json

### AgentCore/Json/JsonSchemaBuilder.cs

Fluent builder for creating JSON schemas from .NET types.

**Key Class:**

```csharp
public class JsonSchemaBuilder
```

**Builder Methods:**

```csharp
public JsonSchemaBuilder AddProperty(string name, JsonSchema schema)
public JsonSchemaBuilder SetType(string type)
public JsonSchemaBuilder SetRequired(bool required)
public JsonSchemaBuilder SetEnum(IEnumerable<object> values)
public JsonSchemaBuilder SetMinimum(double value)
public JsonSchemaBuilder SetMaximum(double value)
public JsonSchemaBuilder SetMinLength(int length)
public JsonSchemaBuilder SetMaxLength(int length)
public JsonSchemaBuilder SetPattern(string regex)
public JsonSchemaBuilder SetItems(JsonSchema items)
public JsonSchemaBuilder SetAdditionalProperties(bool allowed)
```

**Output:**

```csharp
public class JsonSchema
```

- `Type` - "object", "string", "number", "array", "boolean", "null"
- `Properties` - Child properties
- `Required` - Required property names
- `Enum` - Allowed values
- `Items` - For arrays
- `Minimum`, `Maximum` - Numeric bounds
- `AdditionalProperties` - Whether extra properties allowed

**Usage:**

```csharp
var schema = new JsonSchemaBuilder()
    .SetType("object")
    .AddProperty("name", new JsonSchema { Type = "string" })
    .AddProperty("age", new JsonSchema { Type = "integer" })
    .SetRequired(["name"])
    .Build();
```

**Location:** `AgentCore/Json/JsonSchemaBuilder.cs`

---

### AgentCore/Json/JsonSchemaExtensions.cs

Generates JSON schemas from CLR types and validates JSON.

**Key Methods:**

```csharp
public static class JsonSchemaExtensions
```

**Schema Generation:**

```csharp
public static JsonSchema GetSchemaForType(Type type)
```

Generates schema from .NET type:

1. **Primitives** (int, string, bool) → Basic JSON types
2. **Arrays** → `type: "array"`, `items: GetSchemaForType(elementType)`
3. **Objects** → `type: "object"`, properties from public properties
4. **Nullable** → Union of base type and null
5. **Enums** → `type: "string"`, `enum: [values]`
6. **Dictionaries** → `type: "object"`, additionalProperties

**Attributes Recognized:**

- `[Required]` → Required property
- `[Description]` → Property description
- `[JsonPropertyName]` → Property name
- `[JsonIgnore]` → Skip property

**Validation:**

```csharp
public static ValidationResult Validate(string json, JsonSchema schema)
```

- Parses JSON
- Validates against schema
- Returns `ValidationResult` with errors

```csharp
public class ValidationResult
```

- `IsValid` - Whether validation passed
- `Errors` - List of validation errors

**Location:** `AgentCore/Json/JsonSchemaExtensions.cs`

---

### AgentCore/Json/JsonExtensions.cs

General JSON utilities.

**Key Methods:**

```csharp
public static class JsonExtensions
```

**FindAllJsonObjects:**

```csharp
public static IEnumerable<JsonElement> FindAllJsonObjects(string text)
```

Extracts all JSON objects from text without regex:

1. Uses `JsonDocument.TryParseValue` with lenient settings
2. Yields all root-level objects
3. Skips invalid JSON

**NormalizeArgs:**

```csharp
public static string NormalizeArgs(string args)
```

Canonicalizes JSON arguments:

1. Parses JSON
2. Re-serializes with sorted keys and formatting
3. Returns consistent string representation

**Location:** `AgentCore/Json/JsonExtensions.cs`

---

## AgentCore/Providers

### AgentCore/Providers/ILLMStreamProvider.cs

Abstract interface for LLM providers.

**Key Interface:**

```csharp
public interface ILLMStreamProvider
```

- `StreamAsync(LLMRequest request, Action<LLMStreamChunk> onChunk, CancellationToken ct)`
- Returns `Task`

**Configuration:**

```csharp
public class LLMInitOptions
```

- `BaseUrl` - API endpoint
- `ApiKey` - Authentication key
- `Model` - Model identifier
- `Timeout` - Request timeout
- `Headers` - Custom HTTP headers

**Location:** `AgentCore/Providers/ILLMStreamProvider.cs`

---

### AgentCore/Providers/OpenAI/OpenAILLMClient.cs

OpenAI-compatible LLM provider implementation.

**Key Class:**

```csharp
public class OpenAILLMClient : ILLMStreamProvider
```

**Implementation Details:**

1. Uses OpenAI .NET SDK (`OpenAI.ClientCore`)
2. Creates `OpenAIClient` with API key and base URL
3. Calls `GetChatCompletionsStreamingAsync`

**Streaming:**

```csharp
public async Task StreamAsync(LLMRequest request, Action<LLMStreamChunk> onChunk, CancellationToken ct)
```

1. Converts `LLMRequest` to OpenAI format:
   - `ChatCompletionsOptions`
   - Maps messages to `ChatRequestMessage`
   - Includes tools as `ChatFunctionToolDefinition`
2. Streams from OpenAI API
3. Converts each streaming chunk to `LLMStreamChunk`:
   - `ChatCompletionsChunk` → `LLMStreamChunk`
   - Delta content → Text/ToolCall
   - Usage → Usage
   - Finish → Finish

**Error Handling:**

- Converts OpenAI exceptions to framework exceptions
- Handles rate limits, timeouts, auth failures

**Location:** `AgentCore/Providers/OpenAI/OpenAILLMClient.cs`

---

### AgentCore/Providers/OpenAI/OpenAIExtensions.cs

Helpers for converting between AgentCore and OpenAI types.

**Key Methods:**

```csharp
public static class OpenAIExtensions
```

- `ToOpenAI(ChatMessage)` - Convert to OpenAI message
- `ToOpenAI(Tool)` - Convert to function definition
- `ToOpenAI(LLMGenerationOptions)` - Convert options
- `ToAgentCore(ChatResponseMessage)` - Convert response
- `ToAgentCore(ChatTokenUsage)` - Convert usage

**Location:** `AgentCore/Providers/OpenAI/OpenAIExtensions.cs`

---

## AgentCore/Utils

### AgentCore/Utils/HelperExtensions.cs

String helper extensions.

**Key Methods:**

```csharp
public static class HelperExtensions
```

**ToSnake:**

```csharp
public static string ToSnake(this string text)
```

Converts to snake_case:

```csharp
"GetUserName" → "get_user_name"
"HTTPRequest" → "http_request"
```

**ToJoinedString:**

```csharp
public static string ToJoinedString<T>(this IEnumerable<T> items, string separator)
```

Joins collection elements with separator.

**Location:** `AgentCore/Utils/HelperExtensions.cs`

---

## Component Interactions

### Request Flow Diagram

```
User Input
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                      LLMAgent.InvokeAsync                    │
├─────────────────────────────────────────────────────────────┤
│  1. Create AgentContext                                     │
│  2. Add system prompt to ScratchPad                        │
│  3. Get IAgentExecutor from DI                             │
│  4. Execute executor                                        │
└─────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                    ToolCallingLoop.ExecuteAsync              │
├─────────────────────────────────────────────────────────────┤
│  for (i = 0; i < MaxIterations; i++)                       │
│    ├─ Build LLMRequest (prompt, tools, options)            │
│    ├─ Get ILLMExecutor from DI                             │
│    ├─ Call executor.ExecuteAsync()                          │
│    │   ├─ ContextManager.Trim() - fit in context window    │
│    │   ├─ Handler.OnRequest() - prepare handlers           │
│    │   ├─ Provider.StreamAsync() - call LLM               │
│    │   │   └─ Stream chunks through handlers              │
│    │   │       ├─ TextHandler - accumulate text            │
│    │   │       ├─ ToolCallHandler - parse tool calls       │
│    │   │       ├─ StructuredHandler - parse JSON output    │
│    │   │       ├─ TokenUsageHandler - track tokens         │
│    │   │       └─ FinishHandler - capture finish reason    │
│    │   └─ Handler.OnResponse() - build final response      │
│    │                                                               │
│    ├─ If no tool call → return response                           │
│    ├─ Else → ToolRuntime.HandleToolCallAsync()                   │
│    │   └─ Invoke tool, handle timeout/errors                      │
│    ├─ Append tool result to ScratchPad                            │
│    └─ Continue loop                                               │
└─────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                   AgentResponse returned                      │
│  { Text: "...", Output: structured_object }                  │
└─────────────────────────────────────────────────────────────┘
```

### Tool Execution Flow

```
LLM Response contains ToolCall
    │
    ▼
ToolCallParser.Validate(toolCall)
    │
    ├─ Lookup tool in ToolRegistryCatalog
    ├─ Parse arguments from JObject to method parameters
    └─ Validate against schema
    │
    ▼
ToolRuntime.InvokeAsync(toolCall)
    │
    ├─ Get Tool from catalog
    ├─ Inject CancellationToken if needed
    ├─ DynamicInvoke the delegate
    └─ Return result (await Task if async)
    │
    ▼
ToolCallResult created and appended to conversation
```

---

## Execution Flows

### Building an Agent

```csharp
var agent = Agent.Create("assistant")
    .WithInstructions("You are a helpful coding assistant")
    .WithModel("gpt-4o")
    .WithMaxIterations(50)
    .WithTools<CodeTools>()           // Register static methods
    .WithTools(new FileSystem())      // Register instance methods
    .WithOutput<CodeAnalysis>()       // Optional structured output
    .AddOpenAI(options => {
        options.ApiKey = Environment.GetEnvironmentVariable("OPENAI_KEY");
        options.Model = "gpt-4o";
    })
    .ConfigureServices(services => {
        // Add custom services
    })
    .Build();
```

**What happens during Build():**

1. Creates `ServiceCollection` with default implementations
2. Registers core services (Memory, TokenManager, ContextManager, etc.)
3. Registers tool handlers (TextHandler, ToolCallHandler, etc.)
4. Registers the ToolRegistryCatalog with all tools
5. Builds the `IServiceProvider`
6. Creates `LLMAgent` with config

### Invoking the Agent

```csharp
var response = await agent.InvokeAsync(
    "Write a function to calculate fibonacci",
    cancellationToken
);
```

**What happens during InvokeAsync():**

1. Creates a new DI scope
2. Generates a unique session ID
3. Creates `AgentContext` with:
   - User input
   - Services (scoped)
   - Cancellation token
   - Stream callback
   - Fresh `ScratchPad` conversation
4. Adds system prompt to scratch pad
5. Gets `IAgentExecutor` (ToolCallingLoop) from DI
6. Executes the loop

### The Agent Loop (ToolCallingLoop)

1. Adds user message to scratch pad
2. Gets `ILLMExecutor` and `IToolCatalog` from DI
3. For each iteration:
   - Builds `LLMRequest` with current conversation
   - Includes available tools
   - Calls `executor.ExecuteAsync()`
   - If no tool call → returns response
   - If tool call → executes tool, appends result, continues

### LLM Execution (LLMExecutor)

1. **Trim Context**: Uses `ContextManager` to fit within token limit
2. **Prepare Handlers**: Calls `OnRequest()` on all handlers
3. **Stream**: Calls provider's `StreamAsync()`
4. **Process Chunks**: Routes chunks to matching handler by `Kind`
5. **Finalize**: Calls `OnResponse()` on all handlers
6. **Retry**: Uses `RetryPolicy` for validation failures

---

## TestApp Reference

The TestApp demonstrates how to use AgentCore in practice.

### TestApp/Program.cs

Entry point that demonstrates various AgentCore features.

**Demonstrates:**

1. Basic agent creation and invocation
2. Streaming responses
3. Custom tool registration
4. File memory for session persistence
5. Structured output types

**Key Examples:**

- Creating an `AgentBuilder`
- Configuring OpenAI
- Adding tools
- Building and invoking agents

**Location:** `TestApp/Program.cs`

---

### TestApp/ChatBotAgent.cs

Sample agent implementation showing how to structure an agent.

**Demonstrates:**

1. Custom agent configuration
2. Pre-configured tools
3. System prompt design
4. Session management

**Location:** `TestApp/ChatBotAgent.cs`

---

### TestApp/BuiltInTools/

Sample tool implementations showing the `[Tool]` attribute pattern.

#### MathTools.cs

```csharp
public class MathTools
{
    [Tool("add", "Add two numbers")]
    public double Add(double a, double b) => a + b;

    [Tool("multiply", "Multiply two numbers")]
    public double Multiply(double a, double b) => a * b;

    [Tool("calculate", "Perform calculation")]
    public async Task<string> Calculate(string expression)
    {
        // Evaluates mathematical expression
    }
}
```

#### WeatherTools.cs

```csharp
public class WeatherTools
{
    [Tool("get_weather", "Get weather for a location")]
    public async Task<WeatherData> GetWeather(string location)
    {
        // Returns simulated weather data
    }
}
```

#### SearchTools.cs

```csharp
public class SearchTools
{
    [Tool("search", "Search the web")]
    public async Task<List<SearchResult>> Search(string query)
    {
        // Returns simulated search results
    }
}
```

#### RAGTools.cs

```csharp
public class RAGTools
{
    [Tool("search_knowledge", "Search knowledge base")]
    public async Task<List<string>> SearchKnowledge(string query)
    {
        // Simulated RAG retrieval
    }
}
```

#### GeoTools.cs

```csharp
public class GeoTools
{
    [Tool("geocode", "Convert address to coordinates")]
    public async Task<GeoLocation> Geocode(string address) { }

    [Tool("distance", "Calculate distance between points")]
    public async Task<double> Distance(string from, string to) { }
}
```

#### ConversionTools.cs

```csharp
public class ConversionTools
{
    [Tool("convert_unit", "Convert between units")]
    public async Task<double> ConvertUnit(double value, string from, string to) { }

    [Tool("convert_currency", "Convert between currencies")]
    public async Task<double> ConvertCurrency(double amount, string from, string to) { }
}
```

---

## Summary

AgentCore provides a **production-ready framework** for building LLM-powered agents with:

- **Tool-calling agents** with automatic function registration and schema generation
- **Streaming support** with extensible handler pipeline
- **Context management** with intelligent trimming
- **Token tracking** with approximation fallback
- **Persistent memory** via file-based storage
- **Structured output** with JSON schema validation
- **Retry logic** for transient failures
- **Full DI integration** for testability and extensibility

The architecture is clean, modular, and follows SOLID principles, making it easy to extend with new LLM providers, handlers, or custom functionality.

---

## Quick Reference

| Component | File | Interface | Implementation |
|-----------|------|-----------|----------------|
| Agent | `Runtime/Agent.cs` | `IAgent` | `LLMAgent` |
| Builder | `Runtime/AgentBuilder.cs` | - | `AgentBuilder` |
| Executor | `Runtime/AgentExecutor.cs` | `IAgentExecutor` | `ToolCallingLoop` |
| Memory | `Runtime/AgentMemory.cs` | `IAgentMemory` | `FileMemory` |
| Tool Runtime | `Tools/ToolRuntime.cs` | `IToolRuntime` | `ToolRuntime` |
| Tool Registry | `Tools/ToolRegistry.cs` | `IToolRegistry`, `IToolCatalog` | `ToolRegistryCatalog` |
| Tool Parser | `Tools/ToolCallParser.cs` | `IToolCallParser` | `ToolCallParser` |
| LLM Executor | `LLM/Execution/LLMExecutor.cs` | `ILLMExecutor` | `LLMExecutor` |
| LLM Provider | `Providers/` | `ILLMStreamProvider` | `OpenAILLMClient` |
| Token Manager | `Tokens/TokenManager.cs` | `ITokenManager` | `TokenManager` |
| Token Counter | `Tokens/TikTokenCounter.cs` | `ITokenCounter` | `TikTokenCounter` |
| Context Manager | `Tokens/ContextManager.cs` | `IContextManager` | `ContextManager` |
| Conversation | `Chat/Conversation.cs` | - | `Conversation` |
| Retry Policy | `LLM/Execution/RetryPolicy.cs` | `IRetryPolicy` | `RetryPolicy` |
