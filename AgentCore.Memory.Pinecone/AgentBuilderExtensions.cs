using AgentCore;
using AgentCore.Memory;
using AgentCore.Memory.Pinecone;
using Microsoft.Extensions.Logging;

namespace AgentCore.Memory.Pinecone;

/// <summary>
/// Extension methods for AgentBuilder to add Pinecone memory support.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Adds Pinecone as the memory store with advanced semantic search capabilities.
    /// Uses MemoryEngine with PineconeMemoryStore for AMFS-style memory management.
    /// </summary>
    /// <param name="builder">The AgentBuilder instance.</param>
    /// <param name="apiKey">Pinecone API key.</param>
    /// <param name="indexName">Pinecone index name.</param>
    /// <param name="llm">LLM provider for MemoryEngine (required for dream/reflection).</param>
    /// <param name="embeddingProvider">Optional embedding provider for semantic search.</param>
    /// <param name="options">Optional MemoryEngine configuration.</param>
    /// <returns>The configured AgentBuilder.</returns>
    public static AgentBuilder AddPineconeMemory(
        this AgentBuilder builder,
        string apiKey,
        string indexName,
        AgentCore.LLM.ILLMProvider llm,
        IEmbeddingProvider? embeddingProvider = null,
        MemoryEngineOptions? options = null)
    {
        var store = new PineconeMemoryStore(apiKey, indexName, embeddingProvider);
        var memory = new MemoryEngine(store, llm, embeddingProvider, null, options);
        return builder.WithMemory(memory);
    }

    /// <summary>
    /// Adds Pinecone as the memory store with custom logger.
    /// </summary>
    public static AgentBuilder AddPineconeMemory(
        this AgentBuilder builder,
        string apiKey,
        string indexName,
        AgentCore.LLM.ILLMProvider llm,
        IEmbeddingProvider? embeddingProvider,
        ILoggerFactory loggerFactory,
        MemoryEngineOptions? options = null)
    {
        var logger = loggerFactory.CreateLogger<PineconeMemoryStore>();
        var memoryLogger = loggerFactory.CreateLogger<MemoryEngine>();
        var store = new PineconeMemoryStore(apiKey, indexName, embeddingProvider, logger);
        var memory = new MemoryEngine(store, llm, embeddingProvider, null, options, memoryLogger);
        return builder.WithMemory(memory);
    }
}
