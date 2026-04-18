using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AgentCore.Conversation;
using AgentCore.LLM;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore.Memory;

/// <summary>
/// The cognitive memory engine. Implements ISemanticMemory with:
/// - 3-strategy parallel retrieval (semantic + keyword + entity graph) fused via RRF
/// - AMFS confidence decay formula with kind-based multipliers
/// - Automatic background dream (consolidation) and prune cycles
/// - Per-session read tracking for outcome feedback
/// - Full provenance via SourceEntryIds, Version, InvalidatedAt
/// </summary>
public sealed class MemoryEngine : ISemanticMemory, IDisposable
{
    private readonly IMemoryStore _store;
    private readonly ILLMProvider _llm;
    private readonly IEmbeddingProvider _embeddings;
    private readonly IGraphStore? _graph;
    private readonly MemoryEngineOptions _options;
    private readonly ILogger<MemoryEngine> _logger;

    // Background processing
    private readonly Channel<string> _workQueue;
    private readonly Task _backgroundLoop;
    private readonly CancellationTokenSource _cts = new();

    // Per-session read tracker: sessionId → set of recalled entry IDs
    // Lives in engine memory (not on entries) — like AMFS's ReadTracker
    private readonly ConcurrentDictionary<string, HashSet<string>> _sessionReads = new();
    private string _currentSession = "default";

    public MemoryEngine(
        IMemoryStore store,
        ILLMProvider llm,
        IEmbeddingProvider? embeddings = null,
        IGraphStore? graph = null,
        MemoryEngineOptions? options = null,
        ILogger<MemoryEngine>? logger = null)
    {
        _store = store;
        _llm = llm;
        _embeddings = embeddings ?? NullEmbeddingProvider.Instance;
        _graph = graph;
        _options = options ?? new MemoryEngineOptions();
        _logger = logger ?? NullLogger<MemoryEngine>.Instance;

        _workQueue = Channel.CreateBounded<string>(new BoundedChannelOptions(_options.BackgroundQueueSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _backgroundLoop = Task.Run(BackgroundLoopAsync);
    }

    // ── IMemory: RecallAsync ────────────────────────────────────────────────

    public async Task<IReadOnlyList<IContent>> RecallAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        // Embed query for semantic retrieval
        float[]? queryEmbedding = null;
        try
        {
            queryEmbedding = await _embeddings.EmbedAsync(query, ct).ConfigureAwait(false);
            if (queryEmbedding.Length == 0) queryEmbedding = null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Embedding failed for recall query — falling back to keyword only.");
        }

        // 3 strategies in parallel
        var semanticTask = queryEmbedding != null
            ? _store.FindAsync(embedding: queryEmbedding, limit: 20, ct: ct)
            : Task.FromResult<IReadOnlyList<MemoryEntry>>([]);

        var keywordTask = _store.FindAsync(text: query, limit: 20, ct: ct);

        var graphTask = RetrieveViaGraphAsync(query, ct);

        await Task.WhenAll(semanticTask, keywordTask, graphTask).ConfigureAwait(false);

        var semantic = semanticTask.Result;
        var keyword = keywordTask.Result;
        var graphEntries = graphTask.Result;

        // RRF fusion
        var fused = FuseRRF(semantic, keyword, graphEntries);

        // Apply AMFS confidence decay filter
        var now = DateTime.UtcNow;
        var confident = fused
            .Where(e => EffectiveConfidence(e, now) >= _options.MinConfidence)
            .ToList();

        _logger.LogDebug("Recall: {Semantic} semantic, {Keyword} keyword, {Graph} graph → {Fused} fused → {Confident} confident",
            semantic.Count, keyword.Count, graphEntries.Count, fused.Count, confident.Count);

        if (confident.Count == 0) return [];

        // Track recalled entries for outcome feedback
        var reads = _sessionReads.GetOrAdd(_currentSession, _ => new HashSet<string>());
        lock (reads)
            foreach (var e in confident)
                reads.Add(e.Id);

        // Bump RecallCount on returned entries (async, best-effort)
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var e in confident) e.RecallCount++;
                await _store.UpsertAsync(confident, CancellationToken.None).ConfigureAwait(false);
            }
            catch { /* non-fatal */ }
        }, CancellationToken.None);

        // Render: observations first (highest density), then by kind, then recency
        var ordered = confident
            .OrderByDescending(e => e.Kind == MemoryKind.Observation)
            .ThenByDescending(e => e.Kind == MemoryKind.Fact)
            .ThenByDescending(e => e.CreatedAt)
            .ToList();

        // Build content with token-budget cap (~4 chars/token)
        var result = new List<IContent>();
        var sb = new StringBuilder();
        int budgetChars = _options.RecallBudget * 4;

        sb.AppendLine("## Agent Memory\n");
        foreach (var entry in ordered)
        {
            var line = entry.Kind == MemoryKind.Observation && !string.IsNullOrEmpty(entry.Name)
                ? $"**[{entry.Kind}] {entry.Name}**: {entry.Content}"
                : $"**[{entry.Kind}]**: {entry.Content}";

            if (sb.Length + line.Length > budgetChars) break;
            sb.AppendLine(line);
        }

        result.Add(new Text(sb.ToString().Trim()));
        return result;
    }

    // ── IMemory: RetainAsync ────────────────────────────────────────────────

    public async Task RetainAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0) return;

        var conversationText = BuildConversationText(messages);
        if (string.IsNullOrWhiteSpace(conversationText)) return;

        // Extract facts via LLM
        List<MemoryEntry> extracted;
        try
        {
            extracted = await ExtractFactsAsync(conversationText, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fact extraction failed — skipping retain.");
            return;
        }

        if (extracted.Count == 0)
        {
            _logger.LogDebug("No facts extracted from turn.");
            return;
        }

        // Embed entries
        await EmbedEntriesAsync(extracted, ct).ConfigureAwait(false);

        // Store
        await _store.UpsertAsync(extracted, ct).ConfigureAwait(false);
        _logger.LogInformation("Retained {Count} memory entries.", extracted.Count);

        // Extract entity triples for graph (optional)
        if (_graph != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var triples = await ExtractTriplesAsync(conversationText, CancellationToken.None).ConfigureAwait(false);
                    if (triples.Count > 0)
                        await _graph.AddAsync(triples, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Graph triple extraction failed.");
                }
            }, CancellationToken.None);
        }

        // Queue background dream if enabled
        if (_options.AutoDreamEnabled)
            _workQueue.Writer.TryWrite("dream");
    }

    // ── IMemory: ReflectAsync ───────────────────────────────────────────────

    public async Task<string> ReflectAsync(string query, CancellationToken ct = default)
    {
        // Step 1: recall relevant memories
        var recalled = await RecallAsync(query, ct).ConfigureAwait(false);
        var memoryContext = recalled.Count > 0
            ? string.Join("\n", recalled.Select(c => c.ForLlm()))
            : "(no relevant memories found)";

        // Step 2: LLM synthesizes an answer
        var systemPrompt = _options.DefaultReflectionPromptResolved;
        var userMessage = $"## Recalled Memories\n{memoryContext}\n\n## Question\n{query}";

        var messages = new List<Message>
        {
            new(Role.System, new Text(systemPrompt)),
            new(Role.User, new Text(userMessage))
        };

        var answer = await CompleteLLMAsync(messages, ct).ConfigureAwait(false);

        // Step 3: persist as an Observation
        var observation = new MemoryEntry
        {
            Kind = MemoryKind.Observation,
            Name = TruncateName(query),
            Content = answer,
            SourceEntryIds = [] // could be populated from recalled entry IDs if needed
        };

        await EmbedEntriesAsync([observation], ct).ConfigureAwait(false);
        await _store.UpsertAsync([observation], ct).ConfigureAwait(false);
        _logger.LogInformation("Reflection created observation: '{Name}'", observation.Name);

        return answer;
    }

    // ── IMemory: CommitOutcomeAsync ─────────────────────────────────────────

    public async Task CommitOutcomeAsync(OutcomeType outcome, CancellationToken ct = default)
    {
        var reads = _sessionReads.GetOrAdd(_currentSession, _ => new HashSet<string>());
        string[] entryIds;
        lock (reads)
            entryIds = [.. reads];

        if (entryIds.Length == 0)
        {
            _logger.LogDebug("CommitOutcome: no entries recalled this session.");
            return;
        }

        // Retrieve the entries that were recalled
        var recalled = await _store.FindAsync(ct: ct).ConfigureAwait(false);
        var affected = recalled.Where(e => entryIds.Contains(e.Id)).ToList();

        if (affected.Count == 0) return;

        // Apply AMFS outcome multiplier to confidence
        foreach (var entry in affected)
        {
            entry.Confidence = outcome switch
            {
                OutcomeType.Success => Math.Min(1.0f, entry.Confidence * 1.1f),
                OutcomeType.MinorFailure => Math.Max(0f, entry.Confidence * 0.95f),
                OutcomeType.Failure => Math.Max(0f, entry.Confidence * 0.8f),
                OutcomeType.CriticalFailure => Math.Max(0f, entry.Confidence * 0.5f),
                _ => entry.Confidence
            };

            if (outcome == OutcomeType.Success)
                entry.OutcomeCount++;
        }

        await _store.UpsertAsync(affected, ct).ConfigureAwait(false);
        _logger.LogInformation("CommitOutcome({Outcome}): adjusted confidence for {Count} entries.", outcome, affected.Count);
    }

    // ── Public: set current session for read tracking ────────────────────────
    public void SetSession(string sessionId) => _currentSession = sessionId;

    // ── Private: AMFS Confidence Decay Formula ──────────────────────────────

    private float EffectiveConfidence(MemoryEntry entry, DateTime now)
    {
        double ageDays = (now - entry.CreatedAt).TotalDays;

        double kindMultiplier = entry.Kind switch
        {
            MemoryKind.Fact => 1.0,
            MemoryKind.Experience => 1.5,
            MemoryKind.Belief => 0.5,
            MemoryKind.Observation => 1.0,
            _ => 1.0
        };

        double recallBoost = 1.0 + Math.Log(1 + entry.RecallCount);
        double outcomeBoost = entry.OutcomeCount > 0 ? 2.0 : 1.0;

        double effectiveHalfLife = _options.DecayHalfLifeDays
            * kindMultiplier
            * recallBoost
            * outcomeBoost;

        double decayed = entry.Confidence * Math.Pow(0.5, ageDays / effectiveHalfLife);
        return (float)Math.Max(0, decayed);
    }

    // ── Private: 3-strategy retrieval via graph ──────────────────────────────

    private async Task<IReadOnlyList<MemoryEntry>> RetrieveViaGraphAsync(string query, CancellationToken ct)
    {
        if (_graph == null) return [];

        try
        {
            // Simple entity extraction from query (first significant words)
            var entity = ExtractEntityFromQuery(query);
            if (string.IsNullOrEmpty(entity)) return [];

            var triples = await _graph.SearchAsync(entity, limit: 10, maxHops: _options.GraphMaxHops, ct: ct)
                .ConfigureAwait(false);

            if (triples.Count == 0) return [];

            // Find entries related to connected entities
            var relatedEntities = triples
                .SelectMany(t => new[] { t.Source, t.Target })
                .Distinct()
                .Where(e => !string.Equals(e, entity, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var tasks = relatedEntities
                .Select(e => _store.FindAsync(text: e, limit: 5, ct: ct))
                .ToList();

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            return results.SelectMany(r => r).DistinctBy(e => e.Id).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Graph retrieval failed.");
            return [];
        }
    }

    private static string ExtractEntityFromQuery(string query)
    {
        // Simple heuristic: take the longest word (likely entity name) from the first 10 words
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(10)
            .Where(w => w.Length > 3)
            .OrderByDescending(w => w.Length)
            .FirstOrDefault() ?? "";
    }

    // ── Private: RRF Fusion ──────────────────────────────────────────────────

    private static List<MemoryEntry> FuseRRF(
        IReadOnlyList<MemoryEntry> semantic,
        IReadOnlyList<MemoryEntry> keyword,
        IReadOnlyList<MemoryEntry> graph,
        int k = 60)
    {
        var scores = new Dictionary<string, (MemoryEntry Entry, float Score)>();

        void AddRanked(IReadOnlyList<MemoryEntry> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                float rrf = 1.0f / (k + i + 1);
                if (scores.TryGetValue(entry.Id, out var existing))
                    scores[entry.Id] = (entry, existing.Score + rrf);
                else
                    scores[entry.Id] = (entry, rrf);
            }
        }

        AddRanked(semantic);
        AddRanked(keyword);
        AddRanked(graph);

        return scores.Values
            .OrderByDescending(x => x.Score)
            .Select(x => x.Entry)
            .ToList();
    }

    // ── Private: LLM Fact Extraction ────────────────────────────────────────

    private async Task<List<MemoryEntry>> ExtractFactsAsync(string conversationText, CancellationToken ct)
    {
        var prompt = string.IsNullOrWhiteSpace(_options.ExtractionPrompt)
            ? MemoryEngineOptions.DefaultExtractionPrompt
            : _options.ExtractionPrompt;

        var messages = new List<Message>
        {
            new(Role.System, new Text(prompt)),
            new(Role.User, new Text(conversationText))
        };

        var json = await CompleteLLMAsync(messages, ct).ConfigureAwait(false);
        return ParseExtractedFacts(json);
    }

    private static List<MemoryEntry> ParseExtractedFacts(string json)
    {
        var results = new List<MemoryEntry>();
        try
        {
            // Strip markdown fences if LLM wrapped them
            var cleaned = json.Trim();
            if (cleaned.StartsWith("```")) cleaned = cleaned.Split('\n').Skip(1).SkipLast(1).Aggregate((a, b) => a + "\n" + b);

            var items = JsonSerializer.Deserialize<List<ExtractedFact>>(cleaned, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (items == null) return results;

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Content)) continue;
                var kind = Enum.TryParse<MemoryKind>(item.Kind, ignoreCase: true, out var k) ? k : MemoryKind.Fact;
                results.Add(new MemoryEntry
                {
                    Kind = kind,
                    Name = TruncateName(item.Name ?? item.Content),
                    Content = item.Content
                });
            }
        }
        catch { /* LLM returned non-JSON — skip */ }
        return results;
    }

    private sealed class ExtractedFact
    {
        public string? Kind { get; set; }
        public string? Name { get; set; }
        public string? Content { get; set; }
    }

    // ── Private: LLM Triple Extraction ──────────────────────────────────────

    private async Task<List<GraphTriple>> ExtractTriplesAsync(string text, CancellationToken ct)
    {
        var messages = new List<Message>
        {
            new(Role.System, new Text(MemoryEngineOptions.DefaultEntityExtractionPrompt)),
            new(Role.User, new Text(text))
        };

        var json = await CompleteLLMAsync(messages, ct).ConfigureAwait(false);
        return ParseTriples(json);
    }

    private static List<GraphTriple> ParseTriples(string json)
    {
        var results = new List<GraphTriple>();
        try
        {
            var cleaned = json.Trim();
            if (cleaned.StartsWith("```")) cleaned = cleaned.Split('\n').Skip(1).SkipLast(1).Aggregate((a, b) => a + "\n" + b);

            var items = JsonSerializer.Deserialize<List<TripleDto>>(cleaned, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (items == null) return results;
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Source) || string.IsNullOrWhiteSpace(item.Target)) continue;
                results.Add(new GraphTriple(item.Source!, item.Relation ?? "related_to", item.Target!, item.Weight));
            }
        }
        catch { /* skip */ }
        return results;
    }

    private sealed class TripleDto
    {
        public string? Source { get; set; }
        public string? Relation { get; set; }
        public string? Target { get; set; }
        public float Weight { get; set; } = 1.0f;
    }

    // ── Private: Background Loop (Dream + Prune) ─────────────────────────────

    private async Task BackgroundLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(
            Math.Max(100, _options.ConsolidationDebounceMs)));

        string? pendingWork = null;

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                // Try to read from queue (non-blocking)
                if (_workQueue.Reader.TryRead(out var work))
                {
                    pendingWork = work;
                    continue; // debounce: keep reading until queue is quiet
                }

                // If we have pending work and timer fired, execute it
                if (pendingWork != null && await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
                {
                    if (pendingWork == "dream")
                        await DreamAsync(_cts.Token).ConfigureAwait(false);
                    pendingWork = null;
                }
                else if (pendingWork == null)
                {
                    await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false);
                    await PruneAsync(_cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background memory loop error.");
            }
        }
    }

    private async Task DreamAsync(CancellationToken ct)
    {
        try
        {
            // Get all non-observation entries for consolidation
            var all = await _store.FindAsync(
                limit: 200,
                kinds: [MemoryKind.Fact, MemoryKind.Experience, MemoryKind.Belief],
                ct: ct).ConfigureAwait(false);

            if (all.Count < _options.MinFactsForConsolidation)
            {
                _logger.LogDebug("Dream skipped: only {Count} facts (min: {Min}).", all.Count, _options.MinFactsForConsolidation);
                return;
            }

            // Simple clustering: take all recent facts and consolidate
            // More sophisticated: group by Name similarity, but single-call is good enough
            var factTexts = all
                .OrderByDescending(e => e.CreatedAt)
                .Take(30)
                .Select(e => $"- [{e.Kind}] {e.Content}")
                .ToList();

            var messages = new List<Message>
            {
                new(Role.System, new Text(MemoryEngineOptions.DefaultDreamPrompt)),
                new(Role.User, new Text(string.Join("\n", factTexts)))
            };

            var synthesis = await CompleteLLMAsync(messages, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(synthesis)) return;

            var sourceIds = all.Take(30).Select(e => e.Id).ToArray();

            // Check if an observation with same source IDs already exists (update pattern)
            var existing = await _store.FindAsync(
                text: null, kinds: [MemoryKind.Observation], limit: 50, ct: ct).ConfigureAwait(false);

            var existingMatch = existing.FirstOrDefault(o =>
                o.SourceEntryIds.Length > 0 &&
                o.SourceEntryIds.Intersect(sourceIds).Count() > sourceIds.Length / 2);

            MemoryEntry observation;
            if (existingMatch != null)
            {
                existingMatch.Content = synthesis;
                existingMatch.Name = $"Dream consolidation ({DateTime.UtcNow:yyyy-MM-dd})";
                observation = existingMatch;
            }
            else
            {
                observation = new MemoryEntry
                {
                    Kind = MemoryKind.Observation,
                    Name = $"Dream consolidation ({DateTime.UtcNow:yyyy-MM-dd})",
                    Content = synthesis,
                    SourceEntryIds = sourceIds
                };
            }

            await EmbedEntriesAsync([observation], ct).ConfigureAwait(false);
            await _store.UpsertAsync([observation], ct).ConfigureAwait(false);
            _logger.LogInformation("Dream: consolidated {Count} facts into observation '{Name}'.", sourceIds.Length, observation.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dream cycle failed.");
        }
    }

    private async Task PruneAsync(CancellationToken ct)
    {
        try
        {
            var all = await _store.FindAsync(limit: 500, ct: ct).ConfigureAwait(false);
            var now = DateTime.UtcNow;
            var toInvalidate = all
                .Where(e => e.InvalidatedAt == null && EffectiveConfidence(e, now) < _options.PruneThreshold)
                .ToList();

            if (toInvalidate.Count == 0) return;

            foreach (var e in toInvalidate)
                e.InvalidatedAt = now;

            await _store.UpsertAsync(toInvalidate, ct).ConfigureAwait(false);
            _logger.LogInformation("Pruned {Count} entries below confidence threshold {Threshold}.",
                toInvalidate.Count, _options.PruneThreshold);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Prune cycle failed.");
        }
    }

    // ── Private: Helpers ─────────────────────────────────────────────────────

    private async Task EmbedEntriesAsync(IReadOnlyList<MemoryEntry> entries, CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            try
            {
                var embedding = await _embeddings.EmbedAsync(entry.Content, ct).ConfigureAwait(false);
                if (embedding.Length > 0) entry.Embedding = embedding;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Embedding failed for entry '{Id}' — stored without vector.", entry.Id);
            }
        }
    }

    private async Task<string> CompleteLLMAsync(IReadOnlyList<Message> messages, CancellationToken ct)
    {
        var options = new LLMOptions
        {
            Model = _options.Model,
            ToolCallMode = ToolCallMode.None
        };

        var sb = new StringBuilder();
        await foreach (var delta in _llm.StreamAsync(messages, options, tools: null, ct).ConfigureAwait(false))
        {
            if (delta is TextDelta td) sb.Append(td.Value);
        }
        return sb.ToString().Trim();
    }

    private static string BuildConversationText(IReadOnlyList<Message> messages)
    {
        var sb = new StringBuilder();
        foreach (var msg in messages)
        {
            if (msg.Role is Role.System) continue;
            var roleStr = msg.Role == Role.User ? "User" : msg.Role == Role.Assistant ? "Assistant" : "Tool";
            var content = string.Join(" ", msg.Contents.Select(c => c.ForLlm()).Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(content))
                sb.AppendLine($"[{roleStr}]: {content}");
        }
        return sb.ToString();
    }

    private static string TruncateName(string name, int max = 60)
        => name.Length <= max ? name : name[..max];

    public void Dispose()
    {
        _cts.Cancel();
        _workQueue.Writer.TryComplete();
        try { _backgroundLoop.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
