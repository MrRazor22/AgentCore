using AgentCore.Chat;
using AgentCore.Tools;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AgentCore.Tests.Tools
{
    public sealed class ToolRuntime_Tests
    {
        private readonly ToolRuntime _runtime;
        private readonly ToolRuntime _runtimeWithTimeout;

        public ToolRuntime_Tests()
        {
            var catalog = new ToolRegistryCatalog();
            catalog.Register(
                (int a, int b) => a + b,
                (CancellationToken ct) => ct.IsCancellationRequested ? 1 : 0,
                (int? x) => x,
                () => (string?)null,
                async (int x, CancellationToken ct) =>
                {
                    await Task.Delay(50, ct);
                    return x * 2;
                },
                async (CancellationToken ct) =>
                {
                    await Task.Delay(50, ct);
                    throw new InvalidOperationException("boom");
                }
            );

            _runtime = new ToolRuntime(catalog);

            _runtimeWithTimeout = new ToolRuntime(
                catalog,
                Options.Create(new ToolRuntimeOptions
                {
                    ExecutionTimeout = TimeSpan.FromMilliseconds(10)
                }));
        }

        [Fact]
        public async Task InvokeAsync_SyncTool_Works()
        {
            var call = new ToolCall("1", "Sum", JObject.Parse(@"{""a"":1,""b"":2}"));
            var result = await _runtime.InvokeAsync(call);
            Assert.Equal(3, result);
        }

        [Fact]
        public async Task InvokeAsync_TaskTool_ReturnsResult()
        {
            var call = new ToolCall("1", "InvokeAsync", JObject.Parse(@"{""x"":3}"));
            var result = await _runtime.InvokeAsync(call);
            Assert.Equal(6, result);
        }

        [Fact]
        public async Task InvokeAsync_NullReturn_Works()
        {
            var call = new ToolCall("1", "InvokeAsync", JObject.Parse("{}"));
            var result = await _runtime.InvokeAsync(call);
            Assert.Null(result);
        }

        [Fact]
        public async Task InvokeAsync_CancellationToken_Injected()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var call = new ToolCall("1", "InvokeAsync", JObject.Parse("{}"));

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                _runtime.InvokeAsync(call, cts.Token));
        }

        [Fact]
        public async Task InvokeAsync_NoCancellationParam_Works()
        {
            var call = new ToolCall("1", "Sum", JObject.Parse(@"{""a"":2,""b"":3}"));
            var result = await _runtime.InvokeAsync(call, CancellationToken.None);
            Assert.Equal(5, result);
        }

        [Fact]
        public async Task InvokeAsync_ToolThrows_ExceptionWrapped()
        {
            var call = new ToolCall("1", "InvokeAsync", JObject.Parse("{}"));

            var ex = await Assert.ThrowsAsync<ToolExecutionException>(() =>
                _runtime.InvokeAsync(call));

            Assert.Equal("InvokeAsync", ex.ToolName);
            Assert.Contains("boom", ex.Message);
        }

        [Fact]
        public async Task InvokeAsync_UnregisteredTool_Throws()
        {
            var call = new ToolCall("1", "Missing", JObject.Parse("{}"));

            await Assert.ThrowsAsync<ToolExecutionException>(() =>
                _runtime.InvokeAsync(call));
        }

        [Fact]
        public async Task HandleToolCallAsync_TextOnly_ReturnsNullResult()
        {
            var call = new ToolCall("1", null!, null!)
            {
                Message = "just text"
            };

            var result = await _runtime.HandleToolCallAsync(call);
            Assert.Null(result.Result);
        }

        [Fact]
        public async Task HandleToolCallAsync_Exception_ReturnedAsResult()
        {
            var call = new ToolCall("1", "InvokeAsync", JObject.Parse("{}"));
            var result = await _runtime.HandleToolCallAsync(call);

            Assert.IsType<ToolExecutionException>(result.Result);
        }

        [Fact]
        public async Task InvokeAsync_NullableParam_Works()
        {
            var call = new ToolCall("1", "InvokeAsync", JObject.Parse(@"{""x"":null}"));
            var result = await _runtime.InvokeAsync(call);
            Assert.Null(result);
        }

        // ---------------- NEW, REQUIRED TESTS ----------------

        [Fact]
        public async Task HandleToolCallAsync_TimesOut_Returns_ToolExecutionException()
        {
            var call = new ToolCall("1", "InvokeAsync", JObject.Parse(@"{""x"":5}"));

            var result = await _runtimeWithTimeout.HandleToolCallAsync(call);

            var ex = Assert.IsType<ToolExecutionException>(result.Result);
            Assert.Contains("timed out", ex.Message);
        }

        [Fact]
        public async Task HandleToolCallAsync_UserCancellation_Propagates()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var call = new ToolCall("1", "InvokeAsync", JObject.Parse(@"{""x"":5}"));

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                _runtimeWithTimeout.HandleToolCallAsync(call, cts.Token));
        }
    }
}
