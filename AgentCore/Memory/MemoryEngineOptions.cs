namespace AgentCore.Memory;

/// <summary>
/// Flat configuration for MemoryEngine. All fields have sensible defaults.
/// </summary>
public sealed class MemoryEngineOptions
{
    /// <summary>LLM model to use for fact extraction, dreaming, and reflection. Null = use provider default.</summary>
    public string? Model { get; set; }

    /// <summary>
    /// Scope/namespace for this memory engine instance. Used to partition entries per-user or per-agent.
    /// E.g., "user_123", "agent_assistant", "default".
    /// </summary>
    public string Scope { get; set; } = "default";

    /// <summary>If true, background loop auto-runs DreamAsync after Retain to consolidate facts into observations.</summary>
    public bool AutoDreamEnabled { get; set; } = true;

    /// <summary>Base half-life in days for confidence decay. Default: 30 days.</summary>
    public double DecayHalfLifeDays { get; set; } = 30.0;

    /// <summary>
    /// Minimum effective confidence for an entry to appear in recall results.
    /// Entries below this threshold are filtered out (but not deleted).
    /// </summary>
    public float MinConfidence { get; set; } = 0.1f;

    /// <summary>
    /// Effective confidence below which entries are soft-deleted during PruneAsync.
    /// Must be less than MinConfidence.
    /// </summary>
    public float PruneThreshold { get; set; } = 0.05f;

    /// <summary>Maximum token budget for content returned by RecallAsync (approximate, using 4 chars/token).</summary>
    public int RecallBudget { get; set; } = 4096;

    /// <summary>Minimum number of facts required before DreamAsync will consolidate into an Observation.</summary>
    public int MinFactsForConsolidation { get; set; } = 10;

    /// <summary>Debounce delay in milliseconds before background dream triggers after a Retain call.</summary>
    public int ConsolidationDebounceMs { get; set; } = 5000;

    /// <summary>Capacity of the background work queue (bounded channel).</summary>
    public int BackgroundQueueSize { get; set; } = 100;

    /// <summary>Cosine similarity threshold for contradiction detection. Above this = supersede old entry.</summary>
    public float ContradictionThreshold { get; set; } = 0.85f;

    /// <summary>
    /// Custom system prompt for fact extraction. Leave empty to use the built-in prompt.
    /// Must instruct the LLM to return a JSON array of {kind, name, content} objects.
    /// </summary>
    public string ExtractionPrompt { get; set; } = "";

    // ── Internal built-in prompts ──────────────────────────────────────────

    internal string DefaultReflectionPromptResolved => MemoryEngineOptions.DefaultReflectionPrompt;

    internal const string DefaultExtractionPrompt = """
        You are a memory extraction assistant. Given a conversation, extract key pieces of knowledge the agent should remember.
        
        For each piece of knowledge, output a JSON object with:
        - "kind": one of "Fact", "Experience", "Belief", "Skill"
        - "name": a short label/key (max 50 chars)
        - "content": the knowledge to store (1-3 sentences, self-contained)
        
        KIND RULES:
        - Fact: verified knowledge, entity relationships, preferences. Always use canonical names.
          For entity relationships, embed them naturally: "Alice works at Google as a backend engineer."
          For preferences: "User prefers dark mode."
        - Experience: event that happened with context and outcome. "Deployment failed due to missing env vars on Jan 15."
        - Belief: uncertain/hypothetical. "User might prefer TypeScript over JavaScript."
        - Skill: repeatable workflow with 2+ steps. Content = JSON: {"trigger":"...", "steps":[{"step":1,"action":"...","detail":"..."}]}

        IMPORTANT:
        - Extract entity relationships AS Facts. If someone says "I work at Google" extract:
          {"kind":"Fact","name":"user_employer","content":"User works at Google."}
        - Use canonical entity names consistently (e.g., always "Google" not "google" or "Alphabet").
        - Extract causal relationships as Facts: "Scheduling conflicts on Tuesdays have caused stress."

        Return ONLY a valid JSON array. No markdown fences, no explanation. Example:
        [{"kind":"Fact","name":"user_employer","content":"User works at Google as a backend engineer."},
         {"kind":"Fact","name":"google_stack","content":"Google's backend uses Kubernetes and Go."},
         {"kind":"Experience","name":"deploy_issue","content":"Deployment to prod failed due to missing env vars on 2025-01-15."}]
        
        Extract Fact/Experience/Belief normally. Only extract Skill when someone describes
        a clear multi-step process they perform or recommend. Don't force it. Skip greetings, filler, and ephemeral details.
        """;

    internal const string DefaultReflectionPrompt = """
        You are a deep reasoning assistant with access to the agent's memory.
        Given the recalled memories and the question, synthesize a clear, insightful answer.
        Be specific, cite relevant memories, and identify patterns or contradictions.
        """;

    internal const string DefaultDreamPrompt = """
        You are a memory consolidation assistant. Given a set of related facts and experiences,
        synthesize them into a concise, enriched observation that captures the core insight.
        The observation should be more valuable than any individual fact — identify patterns,
        draw conclusions, and surface non-obvious connections.
        If you see entity relationships (e.g., "Alice works at Google", "Google uses Kubernetes"),
        synthesize across them: "Alice likely works with Kubernetes through her role at Google."
        Return ONLY the synthesized observation text (1-4 sentences). No JSON, no markdown.
        """;

    internal const string DefaultSkillEvolutionPrompt = """
        You are a procedure improvement assistant. Given a failed procedure and the failure context,
        output an IMPROVED version of the procedure that addresses the failure.
        
        Rules:
        - Keep the same JSON format: {"trigger":"...", "steps":[{"step":N,"action":"...","detail":"..."}]}
        - Add, modify, or reorder steps to prevent the same failure
        - Keep the trigger description accurate
        - Be specific about what changed and why
        - Return ONLY the JSON object. No explanation.
        """;
}
