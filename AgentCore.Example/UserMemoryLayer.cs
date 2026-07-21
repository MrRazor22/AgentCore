using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using System.Text.Json.Nodes;
using AgentCore.LLM.Schema;

namespace AgentCore.Example;

public class UserMemoryLayer : IMemory
{
    private readonly IMemory _inner;
    private readonly ILLM _extractor;
    private readonly string _filePath;
    private readonly string _extractionPrompt;
    private readonly ILogger<UserMemoryLayer> _logger;
    private readonly List<string> _facts = new();
    private readonly object _lock = new();

    private static readonly JsonSchema _responseSchema = new JsonSchemaBuilder()
        .Type("object")
        .AddProperty("facts", new JsonSchemaBuilder()
            .Type("array")
            .Items(new JsonSchemaBuilder().Type("string").Build())
            .Build(), required: true)
        .AdditionalProperties(false)
        .Build();

    public UserMemoryLayer(
        IMemory inner,
        ILLM extractor,
        string filePath,
        ILoggerFactory? loggerFactory = null,
        string? extractionPrompt = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _logger = loggerFactory?.CreateLogger<UserMemoryLayer>() ?? NullLogger<UserMemoryLayer>.Instance;

        _extractionPrompt = extractionPrompt ?? 
            "You are a background context extraction assistant. Analyze this conversation turn and extract any persistent user preferences, system details, workspace setups, or important facts learned about the environment. Update/merge them with the existing known facts. If a fact is no longer true, remove it. Respond ONLY with a raw JSON object containing a \"facts\" array of strings matching: {{ \"facts\": [\"Fact 1\", \"Fact 2\"] }}. Do not output markdown code blocks or conversational logs. Existing facts:\n{0}\n\nNew turn:\n{1}";

        LoadFacts();
    }

    public IReadOnlyList<string> Facts
    {
        get
        {
            lock (_lock)
            {
                return _facts.ToList();
            }
        }
    }

    private void LoadFacts()
    {
        lock (_lock)
        {
            _facts.Clear();
            if (File.Exists(_filePath))
            {
                try
                {
                    var json = File.ReadAllText(_filePath);
                    var list = JsonSerializer.Deserialize<List<string>>(json);
                    if (list != null)
                    {
                        _facts.AddRange(list);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load learned facts from {Path}. Initializing empty.", _filePath);
                }
            }
        }
    }

    private async Task SaveFactsAsync(CancellationToken ct)
    {
        List<string> copy;
        lock (_lock)
        {
            copy = _facts.ToList();
        }
        var json = JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json, ct).ConfigureAwait(false);
    }

    public async Task<List<Message>> PrepareAsync(Message newInput, CancellationToken ct = default)
    {
        var baseMessages = await _inner.PrepareAsync(newInput, ct).ConfigureAwait(false);

        List<string> activeFacts;
        lock (_lock)
        {
            activeFacts = _facts.ToList();
        }

        if (activeFacts.Count > 0)
        {
            var factsText = "Known facts & user preferences:\n" + string.Join("\n", activeFacts.Select(f => $"- {f}"));
            var factsMessage = new Message(Role.System, new Text(factsText));

            var systemIndex = baseMessages.FindIndex(m => m.Role == Role.System);
            if (systemIndex >= 0)
            {
                baseMessages.Insert(systemIndex + 1, factsMessage);
            }
            else
            {
                baseMessages.Insert(0, factsMessage);
            }
        }

        return baseMessages;
    }

    public async Task RememberAsync(IReadOnlyList<Message> completedTurn, CancellationToken ct = default)
    {
        await _inner.RememberAsync(completedTurn, ct).ConfigureAwait(false);

        // Run background extraction asynchronously to avoid blocking the main loop
        _ = Task.Run(async () =>
        {
            try
            {
                await ExtractAndUpdateFactsAsync(completedTurn, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed background context fact extraction.");
            }
        });
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            _facts.Clear();
        }
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
        await _inner.ClearAsync(ct).ConfigureAwait(false);
    }

    public async Task RestoreAsync(IReadOnlyList<Message> history, CancellationToken ct = default)
    {
        await _inner.RestoreAsync(history, ct).ConfigureAwait(false);
        LoadFacts();
    }

    private async Task ExtractAndUpdateFactsAsync(IReadOnlyList<Message> turn, CancellationToken ct)
    {
        string existingFactsStr;
        lock (_lock)
        {
            existingFactsStr = _facts.Count > 0 
                ? string.Join("\n", _facts.Select(f => $"- {f}")) 
                : "(No existing facts)";
        }

        var turnStr = string.Join("\n", turn.Select(m => $"{m.Role}: {string.Join("\n", m.Contents.Select(c => c.ForLlm()))}"));
        var systemPrompt = new Message(Role.System, new Text(string.Format(_extractionPrompt, existingFactsStr, turnStr)));
        var messages = new List<Message> { systemPrompt };

        var sb = new System.Text.StringBuilder();
        var options = new LLMOptions { ResponseSchema = _responseSchema };
        await foreach (var evt in _extractor.StreamAsync(messages, options: options, tools: null, ct: ct).ConfigureAwait(false))
        {
            if (evt is Text t)
            {
                sb.Append(t.Value);
            }
        }

        var rawResult = sb.ToString().Trim();
        
        // Clean JSON formatting if model outputs markdown blocks
        var cleaned = Regex.Match(rawResult, @"\{[\s\S]*\}").Value;
        if (string.IsNullOrEmpty(cleaned))
        {
            cleaned = rawResult;
        }

        try
        {
            var doc = JsonNode.Parse(cleaned);
            var factsNode = doc?["facts"] as JsonArray;
            if (factsNode != null)
            {
                var newFacts = factsNode.Select(n => n?.ToString()).Where(s => s != null).Cast<string>().ToList();
                lock (_lock)
                {
                    _facts.Clear();
                    _facts.AddRange(newFacts);
                }
                await SaveFactsAsync(ct).ConfigureAwait(false);
                _logger.LogInformation("Background context cognition completed. Extracted {Count} facts.", newFacts.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse context extractor output: {RawResult}", rawResult);
        }
    }
}
