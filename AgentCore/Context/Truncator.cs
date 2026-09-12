using System;
using System.Collections.Generic;
using AgentCore.LLM.Chat;

namespace AgentCore.Context;

public interface ITruncator
{
    IContent Truncate(IContent content, int maxTokens);
}

public class Truncator(
    ITokenizer tokenizer, 
    double headRatio = 0.5, 
    string notice = "\n... [truncated]") : ITruncator
{
    private readonly ITokenizer _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
    private readonly double _headRatio = headRatio is >= 0.0 and <= 1.0 
        ? headRatio 
        : throw new ArgumentOutOfRangeException(nameof(headRatio), "headRatio must be between 0.0 and 1.0.");
    private readonly string _notice = notice ?? string.Empty;

    public virtual IContent Truncate(IContent content, int maxTokens)
    {
        if (_tokenizer.Estimate(content) <= maxTokens)
            return content;

        return content switch
        {
            Text t => new Text(SliceString(t.Value, maxTokens)),
            Reasoning r => new Reasoning(SliceString(r.Value, maxTokens)),
            _ => new Text($"[{content.GetType().Name} omitted: exceeds context budget]")
        };
    }

    protected string SliceString(string text, int maxTokens)
    {
        if (maxTokens <= 0) return string.Empty;

        int maxChars = maxTokens * _tokenizer.CharsPerToken;
        if (maxChars <= _notice.Length)
            return _notice[..Math.Min(_notice.Length, maxChars)];

        int available = maxChars - _notice.Length;
        if (available >= text.Length) return text;

        int head = (int)(available * _headRatio);
        int tail = available - head;

        if (head == 0) return _notice + text[^tail..];
        if (tail == 0) return text[..head] + _notice;
        return text[..head] + _notice + text[^tail..];
    }
}
