using AgentCore.Chat;
using AgentCore.LLM.Protocol;
using AgentCore.LLM.Client;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AgentCore.Tests.LLM
{
    public sealed class RetryPolicy_Tests
    {
        [Fact]
        public async Task Retries_WhenRetryExceptionThrown()
        {
            var opts = Options.Create(new RetryPolicyOptions
            {
                MaxRetries = 1,
                InitialDelay = TimeSpan.Zero
            });

            var policy = new RetryPolicy(null, opts);

            int calls = 0;

            async IAsyncEnumerable<LLMStreamChunk> Fake(Conversation r)
            {
                calls++;
                await Task.Yield();
                if (false) yield break;
                throw new RetryException("fix me");
            }

            var req = new LLMRequest(new Conversation().AddUser("hi"));
            var results = new List<LLMStreamChunk>();

            await foreach (var c in policy.ExecuteStreamAsync(req.Prompt, Fake))
                results.Add(c);

            Assert.Equal(2, calls);
            Assert.Single(results);
        }

        [Fact]
        public async Task Stops_WhenRetriesExhausted()
        {
            var opts = Options.Create(new RetryPolicyOptions
            {
                MaxRetries = 0
            });

            var policy = new RetryPolicy(null, opts);

            int calls = 0;

            async IAsyncEnumerable<LLMStreamChunk> Fake(Conversation r)
            {
                calls++;
                await Task.Yield();
                if (false) yield break;
                throw new RetryException("err");
            }

            var req = new LLMRequest(new Conversation());

            int count = 0;
            await foreach (var _ in policy.ExecuteStreamAsync(req.Prompt, Fake))
                count++;

            Assert.Equal(1, calls);
            Assert.Empty(req.Prompt.Where(m => m.Role == Role.Assistant));
        }

        [Fact]
        public async Task RetryChunk_HasCorrectFormat()
        {
            var opts = Options.Create(new RetryPolicyOptions
            {
                MaxRetries = 1,
                InitialDelay = TimeSpan.Zero
            });

            var policy = new RetryPolicy(null, opts);

            async IAsyncEnumerable<LLMStreamChunk> Fake(Conversation r)
            {
                await Task.Yield();
                if (false) yield break;
                throw new RetryException("oops");
            }

            var req = new LLMRequest(new Conversation().AddUser("x"));
            var items = new List<LLMStreamChunk>();

            await foreach (var c in policy.ExecuteStreamAsync(req.Prompt, Fake))
                items.Add(c);

            Assert.Single(items);
            Assert.Equal("[retry 1] oops", items[0].AsText());
        }

        [Fact]
        public async Task Retry_AddsAssistantMessage_ToWorkingRequest()
        {
            var opts = Options.Create(new RetryPolicyOptions
            {
                MaxRetries = 1,
                InitialDelay = TimeSpan.Zero
            });

            var policy = new RetryPolicy(null, opts);

            Conversation? captured = null;

            async IAsyncEnumerable<LLMStreamChunk> Fake(Conversation r)
            {
                captured = r;   // this is the cloned working request
                await Task.Yield();
                if (false) yield break;
                throw new RetryException("fix me");
            }

            var req = new LLMRequest(new Conversation().AddUser("hi"));

            await foreach (var _ in policy.ExecuteStreamAsync(req.Prompt, Fake))
            {
                // ignore retry chunk
            }

            // original request must stay untouched
            Assert.False(req.Prompt.Any(m => m.Role == Role.Assistant));

            // working clone should have been mutated
            Assert.NotNull(captured);
            Assert.Contains(captured, m => m.Role == Role.Assistant);
        }
    }
}
