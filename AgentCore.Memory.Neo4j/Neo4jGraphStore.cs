using AgentCore.Memory;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace AgentCore.Memory.Neo4j;

/// <summary>
/// Neo4j-based implementation of IGraphStore for entity relationship graphs.
/// Stores GraphTriple records as Neo4j nodes and relationships with edge weights.
/// Supports multi-hop entity traversal for Genesys-style cascade retrieval.
/// </summary>
public sealed class Neo4jGraphStore : IGraphStore
{
    private readonly IDriver _driver;
    private readonly ILogger<Neo4jGraphStore> _logger;

    public Neo4jGraphStore(
        string uri,
        string username,
        string password,
        ILogger<Neo4jGraphStore>? logger = null)
    {
        _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(username, password));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<Neo4jGraphStore>.Instance;
    }

    public async Task AddAsync(IReadOnlyList<GraphTriple> triples, CancellationToken ct = default)
    {
        if (triples.Count == 0) return;

        await using var session = _driver.AsyncSession();
        var transaction = await session.BeginTransactionAsync();

        try
        {
            foreach (var triple in triples)
            {
                // Create or merge nodes and relationship
                var query = @"
                    MERGE (s:Entity {name: $source})
                    MERGE (t:Entity {name: $target})
                    MERGE (s)-[r:RELATION {type: $relation}]->(t)
                    SET r.weight = $weight, r.updatedAt = datetime()
                    RETURN s, r, t";

                var parameters = new Dictionary<string, object>
                {
                    ["source"] = triple.Source,
                    ["target"] = triple.Target,
                    ["relation"] = triple.Relation,
                    ["weight"] = triple.Weight
                };

                await transaction.RunAsync(query, parameters);
            }

            await transaction.CommitAsync();
            _logger.LogInformation("Added {Count} graph triples to Neo4j", triples.Count);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<GraphTriple>> SearchAsync(
        string entity,
        int limit = 10,
        int maxHops = 1,
        CancellationToken ct = default)
    {
        await using var session = _driver.AsyncSession();

        // Build Cypher query for multi-hop traversal
        var relationshipPattern = string.Join("", Enumerable.Range(1, maxHops).Select(_ => "-[:RELATION]->"));
        var query = $@"
            MATCH (start:Entity {{name: $entity}}){relationshipPattern}(end:Entity)
            RETURN start.name as source, r.type as relation, end.name as target, r.weight as weight
            LIMIT {limit}";

        var parameters = new Dictionary<string, object>
        {
            ["entity"] = entity
        };

        var cursor = await session.RunAsync(query, parameters);
        var results = new List<GraphTriple>();

        await foreach (var record in cursor)
        {
            results.Add(new GraphTriple(
                record["source"].As<string>(),
                record["relation"].As<string>(),
                record["target"].As<string>(),
                record["weight"].As<float>()
            ));
        }

        _logger.LogDebug("Found {Count} graph triples from Neo4j for entity '{Entity}'", results.Count, entity);
        return results;
    }

    public async ValueTask DisposeAsync()
    {
        await _driver.DisposeAsync();
    }
}
