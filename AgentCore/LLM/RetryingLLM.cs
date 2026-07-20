using AgentCore.LLM.Chat;
using AgentCore.LLM.Exceptions;
using AgentCore.LLM.Schema;
using AgentCore.Tools;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.LLM;

public sealed class RetryingLLM : ILLM
{
    private readonly ILLM _inner;
    private readonly int _maxRetries;

    public RetryingLLM(ILLM inner, int maxRetries = 3)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _maxRetries = maxRetries;
    }

    public LLMCapabilities GetCapabilities() => _inner.GetCapabilities();

    public async IAsyncEnumerable<LLMEvent> StreamAsync(
        IReadOnlyList<Message> messages,
        LLMOptions? options = null,
        IReadOnlyList<Tool>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        int attempt = 0;
        bool hasYielded = false;

        while (true)
        {
            attempt++;
            IAsyncEnumerator<LLMEvent>? enumerator = null;
            try
            {
                bool hasMore = false;
                try
                {
                    var content = _inner.StreamAsync(messages, options, tools, ct);
                    enumerator = content.GetAsyncEnumerator(ct);
                    hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (RetryableException) when (attempt <= _maxRetries && !hasYielded)
                {
                    if (enumerator != null)
                    {
                        await enumerator.DisposeAsync().ConfigureAwait(false);
                        enumerator = null;
                    }

                    int delayMs = (int)(Math.Pow(2, attempt) * 500) + new Random().Next(0, 200);
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                    continue;
                }

                while (hasMore)
                {
                    var item = enumerator.Current;
                    hasYielded = true;
                    yield return item;
                    hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }

                break;
            }
            finally
            {
                if (enumerator != null)
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
