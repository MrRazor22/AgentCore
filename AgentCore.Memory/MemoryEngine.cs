using System.Text;
using System.Text.Json;
using AgentCore.Conversation;
using AgentCore.LLM;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentCore.Memory;

/// <summary>
/// The cognitive memory engine. Implements IAgentMemory with:
/// - 2-strategy parallel retrieval (semantic + keyword) fused via RRF
/// - AMFS confidence decay formula with kind-based multipliers
/// - LLM fact extraction from conversations
/// - Full provenance via SourceEntryIds, Version, InvalidatedAt
/// </summary>
public sealed class MemoryEngine : IAgentMemory, IDisposable
{
    private readonly IMemoryStore _store;
    private readonly ILLMProvider _llm;
    private readonly IEmbeddingProvider _embeddings;
    private readonly MemoryEngineOptions _options;
    private readonly ILogger<MemoryEngine> _logger;


    public MemoryEngine(
        IMemoryStore store,
        ILLMProvider llm,
        IEmbeddingProvider? embeddings = null,
        MemoryEngineOptions? options = null,
        ILogger<MemoryEngine>? logger = null)
    {
        _store = store;
        _llm = llm;
        _embeddings = embeddings ?? NullEmbeddingProvider.Instance;
        _options = options ?? new MemoryEngineOptions();
        _logger = logger ?? NullLogger<MemoryEngine>.Instance;
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

        // 2 strategies in parallel
        var semanticTask = queryEmbedding != null
            ? _store.FindAsync(embedding: queryEmbedding, limit: 20, ct: ct)
            : Task.FromResult<IReadOnlyList<MemoryEntry>>([]);

        var keywordTask = _store.FindAsync(text: query, limit: 20, ct: ct);

        await Task.WhenAll(semanticTask, keywordTask).ConfigureAwait(false);

        var semantic = semanticTask.Result;
        var keyword = keywordTask.Result;

        // RRF fusion
        var fused = FuseRRF(semantic, keyword);

        // Apply AMFS confidence decay filter
        var now = DateTime.UtcNow;
        var confident = fused
            .Where(e => EffectiveConfidence(e, now) >= _options.MinConfidence)
            .ToList();

        _logger.LogDebug("Recall: {Semantic} semantic, {Keyword} keyword → {Fused} fused → {Confident} confident",
            semantic.Count, keyword.Count, fused.Count, confident.Count);

        if (confident.Count == 0) return [];

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
    }

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

    // ── Private: RRF Fusion ──────────────────────────────────────────────────

    private static List<MemoryEntry> FuseRRF(
        IReadOnlyList<MemoryEntry> semantic,
        IReadOnlyList<MemoryEntry> keyword,
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

    public void Dispose() { }
}
