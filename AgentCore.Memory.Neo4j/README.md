# AgentCore.Memory.Neo4j

Neo4j-based implementation of `IGraphStore` for AgentCore's advanced memory system. Provides entity relationship graph storage and multi-hop traversal for Genesys-style cascade retrieval.

## Features

- **Entity Relationship Storage**: Stores `GraphTriple` records (Source, Relation, Target) with confidence weights
- **Multi-hop Traversal**: BFS traversal up to N hops away from a starting entity
- **Cypher Query Support**: Uses Neo4j's native Cypher query language for efficient graph operations
- **Weighted Relationships**: Supports confidence weights for relationship strength scoring
- **Async/Await**: Fully async API for non-blocking operations

## Installation

```bash
dotnet add package AgentCore.Memory.Neo4j
```

Requires Neo4j database instance (local or cloud).

## Quick Start

### Basic Usage (Graph Only)

```csharp
using AgentCore;
using AgentCore.Memory.Neo4j;

var agent = LLMAgent.Create("my-agent")
    .AddNeo4jGraph(
        uri: "bolt://localhost:7687",
        username: "neo4j",
        password: "your-password")
    .WithInstructions("You are a helpful assistant.")
    .AddOpenAI("model", "api-key", "base-url")
    .Build();
```

### With Custom Logger

```csharp
var loggerFactory = LoggerFactory.Create(b => b.AddConsole());

var agent = LLMAgent.Create("my-agent")
    .AddNeo4jGraph(
        uri: "bolt://localhost:7687",
        username: "neo4j",
        password: "your-password",
        loggerFactory: loggerFactory)
    .WithInstructions("You are a helpful assistant.")
    .AddOpenAI("model", "api-key", "base-url")
    .Build();
```

### Combined: Pinecone (Vector) + Neo4j (Graph)

Full 3-strategy retrieval (semantic + keyword + entity traversal):

```csharp
using AgentCore.Memory.Pinecone;
using AgentCore.Memory.Neo4j;

var agent = LLMAgent.Create("my-agent")
    .AddPineconeAndNeo4jMemory(
        pineconeApiKey: "your-pinecone-key",
        pineconeIndexName: "my-index",
        neo4jUri: "bolt://localhost:7687",
        neo4jUsername: "neo4j",
        neo4jPassword: "your-password",
        llm: llmProvider,
        embeddingProvider: embeddingProvider)
    .WithInstructions("You are a helpful assistant.")
    .Build();
```

## How It Works

### Graph Storage

When the agent retains information, `MemoryEngine` extracts entity relationships and stores them as Neo4j nodes and relationships:

```cypher
(:Entity {name: "Alice"})-[:RELATION {type: "KNOWS", weight: 0.9}]->(:Entity {name: "Bob"})
```

### Graph Traversal

When recalling information, the graph store performs multi-hop BFS traversal:

```cypher
MATCH (start:Entity {name: "Alice"})-[:RELATION]->(end:Entity)
RETURN start.name, r.type, end.name, r.weight
```

With `maxHops=2`, this finds direct neighbors AND neighbors of neighbors.

### Integration with MemoryEngine

- **Without Graph**: 2 retrieval strategies (semantic + keyword)
- **With Graph**: 3 retrieval strategies (+ entity traversal)

MemoryEngine fuses results from all strategies using Reciprocal Rank Fusion (RRF).

## Neo4j Setup

### Local Installation

```bash
# Using Docker
docker run -d \
  --name neo4j \
  -p 7474:7474 -p 7687:7687 \
  -e NEO4J_AUTH=neo4j/password \
  neo4j:latest
```

### Cloud (Neo4j Aura)

1. Sign up at [neo4j.com/cloud](https://neo4j.com/cloud/)
2. Create a free AuraDB instance
3. Copy connection URI and credentials

## Configuration Options

```csharp
var options = new MemoryEngineOptions
{
    // Graph traversal settings
    GraphHopLimit = 2,        // Max hops for entity traversal
    GraphResultLimit = 10,    // Max triples to return
    
    // Memory consolidation
    DreamInterval = TimeSpan.FromMinutes(30),
    PruneInterval = TimeSpan.FromHours(6)
};

var agent = LLMAgent.Create("my-agent")
    .AddNeo4jGraph(
        uri: "bolt://localhost:7687",
        username: "neo4j",
        password: "password",
        options: options)
    .Build();
```

## Inspiration

This implementation is inspired by **Genesys**, which uses graph databases for:
- Entity relationship tracking
- Multi-hop knowledge propagation
- Cascade retrieval of connected concepts

## Performance Considerations

- **Indexing**: Neo4j automatically indexes entity names for fast lookups
- **Traversal Depth**: Higher `maxHops` increases query time exponentially
- **Connection Pooling**: The driver manages connection pooling automatically
- **Batch Operations**: `AddAsync` processes triples in a single transaction

## License

Part of AgentCore project. See main LICENSE for details.
