using AgentCore.Chat;
using AgentCore.LLM.Client;
using AgentCore.LLM.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AgentCore.Tests.LLM
{
    public sealed class RetryPolicy_Tests
    {
        private readonly RetryPolicy _policy;

        public RetryPolicy_Tests()
        {
            var logger = Mock.Of<ILogger<RetryPolicy>>();
            var opts = Options.Create(new RetryPolicyOptions { MaxRetries = 2 });
            _policy = new RetryPolicy(logger, opts);
        }

        [Fact]
        public async Task NoRetry_When_Stream_Completes_Normally()
        {
            var convo = new Conversation();
            int calls = 0;

            async IAsyncEnumerable<LLMStreamChunk> Factory(Conversation c)
            {
                calls++;
                yield return new LLMStreamChunk(StreamKind.Text, "ok");
            }

            var chunks = await Collect(_policy.ExecuteStreamAsync(convo, Factory));

            Assert.Single(chunks);
            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task Retry_On_RetryException_Then_Succeeds()
        {
            var convo = new Conversation();
            int calls = 0;

            async IAsyncEnumerable<LLMStreamChunk> Factory(Conversation c)
            {
                calls++;
                if (calls == 1)
                    throw new RetryException("bad tool call");

                yield return new LLMStreamChunk(StreamKind.Text, "fixed");
            }

            var chunks = await Collect(_policy.ExecuteStreamAsync(convo, Factory));

            Assert.Single(chunks);
            Assert.Equal(2, calls);
            Assert.Contains(convo, m => m.Role == Role.Assistant);
        }

        [Fact]
        public async Task Stops_After_MaxRetries_Throws_RetryException()
        {
            var convo = new Conversation();
            int calls = 0;

            IAsyncEnumerable<LLMStreamChunk> Factory(Conversation c)
            {
                calls++;
                throw new RetryException("always bad");
            }

            var ex = await Assert.ThrowsAsync<RetryException>(async () =>
                await Collect(_policy.ExecuteStreamAsync(convo, Factory)));

            Assert.Equal("always bad", ex.Message);
            Assert.Equal(3, calls); // initial + 2 retries
        }

        [Fact]
        public async Task NonRetryException_Bubbles()
        {
            var convo = new Conversation();

            IAsyncEnumerable<LLMStreamChunk> Factory(Conversation c)
            {
                throw new InvalidOperationException("boom");
            }

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await Collect(_policy.ExecuteStreamAsync(convo, Factory)));
        }

        [Fact]
        public async Task Cancellation_Propagates()
        {
            var convo = new Conversation();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            async IAsyncEnumerable<LLMStreamChunk> Factory(Conversation c)
            {
                yield return new LLMStreamChunk(StreamKind.Text, "x");
            }

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await Collect(_policy.ExecuteStreamAsync(convo, Factory, cts.Token)));
        }

        [Fact]
        public async Task Request_Is_Cloned_Per_Attempt()
        {
            var convo = new Conversation();
            var seen = new HashSet<Conversation>();

            IAsyncEnumerable<LLMStreamChunk> Factory(Conversation c)
            {
                Assert.DoesNotContain(c, seen);
                seen.Add(c);
                throw new RetryException("fail");
            }

            await Assert.ThrowsAsync<RetryException>(async () =>
                await Collect(_policy.ExecuteStreamAsync(convo, Factory)));

            Assert.True(seen.Count > 1);
        }

        // ---------- helpers ----------

        private static async Task<List<LLMStreamChunk>> Collect(
            IAsyncEnumerable<LLMStreamChunk> stream)
        {
            var list = new List<LLMStreamChunk>();
            await foreach (var c in stream)
                list.Add(c);
            return list;
        }
    }
}
