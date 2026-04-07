using AgentCore.Conversation;
using AgentCore.Tokens;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AgentCore.Context;

public sealed class ContextAssembler : IContextAssembler
{
    private readonly List<ContextRegistration> _registrations = [];
    private readonly ITokenCounter _tokenCounter;
    private readonly ILogger<ContextAssembler> _logger;

    public ContextAssembler(ITokenCounter tokenCounter, ILogger<ContextAssembler> logger)
    {
        _tokenCounter = tokenCounter;
        _logger = logger;
    }

    public void Register(IContextSource source, int? maxTokenBudget = null)
    {
        _registrations.Add(new ContextRegistration(source, maxTokenBudget));
    }

    public async Task<IReadOnlyList<Message>> AssembleAsync(int availableTokens, CancellationToken ct = default)
    {
        if (_registrations.Count == 0) return Array.Empty<Message>();

        // 1. Collect raw content
        var sourceContents = new List<(ContextRegistration Reg, IReadOnlyList<IContent> Contents, int TokenCount)>();
        foreach (var reg in _registrations)
        {
            var contents = await reg.Source.GetContextAsync(ct);
            if (contents.Count == 0) continue;

            // Simple token estimation (4 chars per token) if ITokenCounter isn't enough/efficient here
            // but we'll try to use ITokenCounter for accuracy.
            var tempMsg = new Message(reg.Source.Role, contents);
            var tokenCount = await _tokenCounter.CountAsync([tempMsg], ct);
            
            sourceContents.Add((reg, contents, tokenCount));
        }

        int totalRawTokens = sourceContents.Sum(x => x.TokenCount);

        // 2. Budget Allocation
        var finalContents = new List<(ContextRegistration Reg, IReadOnlyList<IContent> Contents)>();
        
        if (totalRawTokens <= availableTokens)
        {
            finalContents = sourceContents.Select(x => (x.Reg, x.Contents)).ToList();
        }
        else
        {
            _logger.LogWarning("Context budget exceeded: {Raw}/{Available}. Truncating based on priority.", totalRawTokens, availableTokens);
            
            // Sort by Priority DESC
            var sorted = sourceContents.OrderByDescending(x => x.Reg.Source.Priority).ToList();
            int remaining = availableTokens;
            
            foreach (var item in sorted)
            {
                if (remaining <= 0)
                {
                    _logger.LogDebug("Source '{Name}' dropped — no budget left.", item.Reg.Source.Name);
                    continue;
                }

                int budget = item.Reg.MaxTokenBudget ?? (int)(availableTokens / (float)sorted.Count); 
                int allocated = Math.Min(item.TokenCount, Math.Min(budget, remaining));

                if (allocated < item.TokenCount)
                {
                    _logger.LogInformation("Truncating source '{Name}': {Raw} -> {Allocated}", item.Reg.Source.Name, item.TokenCount, allocated);
                    var text = string.Join("\n", item.Contents.Select(c => c.ForLlm()));
                    // Rough truncation (4 chars/token)
                    int charLimit = Math.Max(0, allocated * 4 - 50);
                    var truncatedText = text.Length > charLimit 
                        ? text.Substring(0, charLimit) + "\n... [truncated, read source for full content]"
                        : text;
                    finalContents.Add((item.Reg, [new Text(truncatedText)]));
                }
                else
                {
                    finalContents.Add((item.Reg, item.Contents));
                }
                
                remaining -= allocated;
            }
        }

        // 3. Merge & Build Messages
        // Group by Role, maintaining order of priority within role
        return finalContents
            .GroupBy(x => x.Reg.Source.Role)
            .Select(g =>
            {
                var roleContents = g.SelectMany(x => x.Contents).ToList();
                return new Message(g.Key, roleContents);
            })
            .ToList();
    }
}
