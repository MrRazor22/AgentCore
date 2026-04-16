# AgentCore.Memory.Pinecone

Pinecone vector database provider for AgentCore's advanced memory system. Implements `IMemoryStore` for semantic search with AMFS-style confidence decay.

## Features

- **Semantic Search**: Vector-based similarity search using cosine similarity
- **Metadata Filtering**: Filter by memory kind, confidence, time ranges
- **AMFS Support**: Full integration with MemoryEngine's confidence decay and outcome tracking
- **Scalable Storage**: Leverages Pinecone's managed vector database
- **Soft Deletes**: Preserves audit history with invalidation tracking

## Installation

```bash
dotnet add package AgentCore.Memory.Pinecone
```

## Quick Start

```csharp
using AgentCore;
using AgentCore.Memory.Pinecone;
using AgentCore.OpenAI; // or your preferred LLM provider

// Setup embedding provider (required for semantic search)
var embeddingProvider = new OpenAIEmbeddingProvider(apiKey);

// Create agent with Pinecone memory
var agent = LLMAgent.Create("chatbot")
    .AddOpenAI(apiKey, "gpt-4")
    .AddPineconeMemory(
        apiKey: "your-pinecone-api-key",
        indexName: "agent-memory",
        embeddingProvider: embeddingProvider)
    .Build();
```

## Configuration

### Basic Setup

```csharp
var agent = LLMAgent.Create("agent")
    .AddPineconeMemory(
        apiKey: Environment.GetEnvironmentVariable("PINECONE_API_KEY"),
        indexName: "my-index")
    .Build();
```

### With Custom MemoryEngine Options

```csharp
var memoryOptions = new MemoryEngineOptions
{
    MinConfidence = 0.3f,
    DecayHalfLifeDays = 30,
    DreamIntervalMinutes = 60
};

var agent = LLMAgent.Create("agent")
    .AddPineconeMemory(
        apiKey: pineconeApiKey,
        indexName: "agent-memory",
        embeddingProvider: embeddingProvider,
        options: memoryOptions)
    .Build();
```

### With Custom Logger

```csharp
var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

var agent = LLMAgent.Create("agent")
    .AddPineconeMemory(
        apiKey: pineconeApiKey,
        indexName: "agent-memory",
        embeddingProvider: embeddingProvider,
        loggerFactory: loggerFactory)
    .Build();
```

## Pinecone Index Requirements

Create a Pinecone index with the following specifications:

- **Metric**: Cosine similarity (recommended) or Euclidean
- **Dimension**: Match your embedding model (e.g., 1536 for OpenAI text-embedding-3-small)
- **Pod Type**: Starter (S1) or Production (P1) depending on your needs

Example using Pinecone CLI:
```bash
pinecone index create agent-memory \
  --dimension 1536 \
  --metric cosine \
  -- pods 1 \
  -- pod-type s1.x1
```

## Advanced Usage

### Direct Store Usage

```csharp
using AgentCore.Memory;
using AgentCore.Memory.Pinecone;

var store = new PineconeMemoryStore(apiKey, "agent-memory", embeddingProvider);

// Upsert memories
var entries = new List<MemoryEntry>
{
    new MemoryEntry
    {
        Content = "The user prefers dark mode in applications",
        Kind = MemoryKind.Fact,
        Confidence = 1.0f
    }
};
await store.UpsertAsync(entries);

// Search memories
var results = await store.FindAsync(
    embedding: await embeddingProvider.EmbedAsync("user preferences"),
    limit: 10,
    kinds: new[] { MemoryKind.Fact });
```

### Memory Engine Integration

```csharp
var store = new PineconeMemoryStore(apiKey, "agent-memory", embeddingProvider);
var memory = new MemoryEngine(store, embeddingProvider);

// Recall (semantic search)
var memories = await memory.RecallAsync("what does the user prefer?");

// Retain (store with embedding)
var messages = new List<Message> { /* conversation messages */ };
await memory.RetainAsync(messages);

// Reflect (deep synthesis)
var reflection = await memory.ReflectAsync("summarize user preferences");

// Commit outcome (AMFS confidence adjustment)
await memory.CommitOutcomeAsync(OutcomeType.Success);
```

## Architecture

This provider follows the AgentCore memory architecture:

- **IMemoryStore**: Persistence layer for vector + metadata storage
- **MemoryEngine**: Cognitive engine with AMFS decay, RRF fusion, dream cycles
- **IAgentMemory**: Simple interface used by agents

PineconeMemoryStore implements `IMemoryStore`, handling:
- Vector upserts with metadata
- Semantic similarity search
- Metadata filtering (kind, time ranges, invalidation)
- Soft delete support

## Inspiration

This implementation draws from:
- **Hindsight**: Vector-based semantic search with metadata filtering
- **AMFS**: Confidence decay, outcome tracking, read counting
- **Genesys**: Entity relationship patterns (via IGraphStore - separate provider)

## License

MIT License - see AgentCore main project for details.
