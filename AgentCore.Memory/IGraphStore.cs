namespace AgentCore.Memory;

/// <summary>
/// Optional entity relationship graph — separate from IMemoryStore (Memo pattern:
/// vector and graph are independent concerns). When provided, MemoryEngine gains a
/// third retrieval strategy: entity traversal with multi-hop support.
/// 
/// No default implementation ships — real graph value requires a real graph DB
/// (Neo4j, FalkorDB, etc.). Without graph: 2 strategies (semantic + keyword).
/// With graph: 3 strategies (+ entity traversal).
/// </summary>
public interface IGraphStore
{
    /// <summary>
    /// Store entity relationships extracted from retained messages.
    /// Called by MemoryEngine.RetainAsync after fact extraction.
    /// </summary>
    Task AddAsync(IReadOnlyList<GraphTriple> triples, CancellationToken ct = default);

    /// <summary>
    /// Traverse the graph from a given entity, up to maxHops hops away.
    /// Enables Genesys-style cascade retrieval: related entities surface connected memories.
    /// </summary>
    /// <param name="entity">Starting entity name to traverse from.</param>
    /// <param name="limit">Maximum number of connected triples to return.</param>
    /// <param name="maxHops">BFS depth. 1 = direct neighbors only. 2 = neighbors of neighbors.</param>
    Task<IReadOnlyList<GraphTriple>> SearchAsync(
        string entity,
        int limit = 10,
        int maxHops = 1,
        CancellationToken ct = default);
}

/// <summary>
/// An entity relationship triple with a confidence weight.
/// Source, Relation, Target pattern (Memo/Hindsight pattern).
/// Weight enables Genesys-style connectivity scoring — higher weight = stronger relationship.
/// </summary>
public record GraphTriple(
    string Source,
    string Relation,
    string Target,
    float Weight = 1.0f);
