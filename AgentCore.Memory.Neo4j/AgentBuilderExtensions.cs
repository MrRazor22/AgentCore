using AgentCore;
using AgentCore.Memory;
using AgentCore.Memory.Neo4j;
using Microsoft.Extensions.Logging;

namespace AgentCore.Memory.Neo4j;

/// <summary>
/// Extension methods for AgentBuilder to add Neo4j graph store support.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Adds Neo4j as the graph store with entity relationship traversal.
    /// Enables Genesys-style cascade retrieval for connected entities.
    /// </summary>
    /// <param name="builder">The AgentBuilder instance.</param>
    /// <param name="uri">Neo4j connection URI (e.g., "bolt://localhost:7687").</param>
    /// <param name="username">Neo4j username.</param>
    /// <param name="password">Neo4j password.</param>
    /// <param name="options">Optional MemoryEngine configuration.</param>
    /// <returns>The configured AgentBuilder.</returns>
    public static AgentBuilder AddNeo4jGraph(
        this AgentBuilder builder,
        string uri,
        string username,
        string password,
        MemoryEngineOptions? options = null)
    {
        var graph = new Neo4jGraphStore(uri, username, password);
        var memory = new MemoryEngine(null!, null!, NullEmbeddingProvider.Instance, graph, options);
        return builder.WithMemory(memory);
    }

    /// <summary>
    /// Adds Neo4j as the graph store with custom logger.
    /// </summary>
    public static AgentBuilder AddNeo4jGraph(
        this AgentBuilder builder,
        string uri,
        string username,
        string password,
        ILoggerFactory loggerFactory,
        MemoryEngineOptions? options = null)
    {
        var logger = loggerFactory.CreateLogger<Neo4jGraphStore>();
        var graph = new Neo4jGraphStore(uri, username, password, logger);
        var memoryLogger = loggerFactory.CreateLogger<MemoryEngine>();
        var memory = new MemoryEngine(null!, null!, NullEmbeddingProvider.Instance, graph, options, memoryLogger);
        return builder.WithMemory(memory);
    }
}
