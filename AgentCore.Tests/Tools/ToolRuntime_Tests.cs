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

        private static class TestTools
        {
            public static int Sum(int a, int b) => a + b;
            public static int? Nullable(int? x) => x;
            public static string? NullReturn() => null;

            public static async Task<int> DoubleAsync(int x, CancellationToken ct)
            {
                await Task.Delay(50, ct);
                return x * 2;
            }

            public static async Task ThrowAsync(CancellationToken ct)
            {
                await Task.Delay(10, ct);
                throw new InvalidOperationException("boom");
            }
        }

        public ToolRuntime_Tests()
        {
            var catalog = new ToolRegistryCatalog();
            catalog.Register(
                (Func<int, int, int>)TestTools.Sum,
                (Func<int?, int?>)TestTools.Nullable,
                (Func<string?>)TestTools.NullReturn,
                (Func<int, CancellationToken, Task<int>>)TestTools.DoubleAsync,
                (Func<CancellationToken, Task>)TestTools.ThrowAsync
            );

            _runtime = new ToolRuntime(catalog);

            _runtimeWithTimeout = new ToolRuntime(
                catalog,
                Options.Create(new ToolRuntimeOptions
                {
                    ExecutionTimeout = TimeSpan.FromMilliseconds(10)
                }));
        }

        private static ToolCall Call(string name, params object[] args)
            => new ToolCall("1", name, new JObject(), args);

        [Fact]
        public async Task InvokeAsync_SyncTool_Works()
        {
            var result = await _runtime.InvokeAsync(
                Call("TestTools.Sum", 1, 2));

            Assert.Equal(3, result);
        }

        [Fact]
        public async Task InvokeAsync_TaskTool_ReturnsResult()
        {
            var result = await _runtime.InvokeAsync(
                Call("TestTools.DoubleAsync", 3));

            Assert.Equal(6, result);
        }

        [Fact]
        public async Task InvokeAsync_NullReturn_Works()
        {
            var result = await _runtime.InvokeAsync(
                Call("TestTools.NullReturn"));

            Assert.Null(result);
        }

        [Fact]
        public async Task InvokeAsync_NullableParam_Works()
        {
            var result = await _runtime.InvokeAsync(
                Call("TestTools.Nullable", null));

            Assert.Null(result);
        }

        [Fact]
        public async Task InvokeAsync_ToolThrows_ExceptionWrapped()
        {
            var ex = await Assert.ThrowsAsync<ToolExecutionException>(() =>
                _runtime.InvokeAsync(Call("TestTools.ThrowAsync")));

            Assert.Equal("TestTools.ThrowAsync", ex.ToolName);
            Assert.Contains("boom", ex.Message);
        }

        [Fact]
        public async Task HandleToolCallAsync_TimesOut_Returns_ToolExecutionException()
        {
            var result = await _runtimeWithTimeout.HandleToolCallAsync(
                Call("TestTools.DoubleAsync", 5));

            var ex = Assert.IsType<ToolExecutionException>(result.Result);
            Assert.IsType<TimeoutException>(ex.InnerException);
        }

        [Fact]
        public async Task HandleToolCallAsync_UserCancellation_Propagates()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                _runtimeWithTimeout.HandleToolCallAsync(
                    Call("TestTools.DoubleAsync", 5),
                    cts.Token));
        }
    }
}
