using AgentCore.Chat;
using AgentCore.Tools;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentCore.Tests.Tools
{
    public class ToolRuntime_Tests
    {
        class FakeCatalog : IToolCatalog
        {
            readonly Dictionary<string, Tool> _tools = new();
            public IReadOnlyList<Tool> RegisteredTools => new List<Tool>(_tools.Values);
            public bool Contains(string name) => _tools.ContainsKey(name);
            public Tool Get(string name) => _tools.TryGetValue(name, out var t) ? t : null;
            public void Add(string name, Delegate fn)
            {
                _tools[name] = new Tool
                {
                    Name = name,
                    Function = fn,
                    ParametersSchema = new JObject()
                };
            }
        }

        ToolCall Call(string name, params object[] args)
        {
            return new ToolCall(
                Guid.NewGuid().ToString(),
                name,
                null,           // arguments null because we bypass schema
                args,           // these are parameters
                null            // no message
            );
        }

        [Fact]
        public async Task InvokeAsync_SyncTool_ReturnsValue()
        {
            var c = new FakeCatalog();
            c.Add("Add", (Func<int, int, int>)((a, b) => a + b));

            var r = new ToolRuntime(c);
            var res = await r.InvokeAsync(Call("Add", 2, 3));

            Assert.Equal(5, res);
        }

        [Fact]
        public async Task InvokeAsync_AsyncVoidTool_ExecutesAndReturnsNull()
        {
            var c = new FakeCatalog();
            c.Add("Ping", (Func<Task>)(async () => await Task.Delay(1)));

            var r = new ToolRuntime(c);
            var res = await r.InvokeAsync(Call("Ping"));

            Assert.Null(res);
        }

        [Fact]
        public async Task InvokeAsync_AsyncReturnTool_ReturnsCorrectValue()
        {
            var c = new FakeCatalog();
            c.Add("Double", (Func<int, Task<int>>)(async x => x * 2));

            var r = new ToolRuntime(c);
            var res = await r.InvokeAsync(Call("Double", 7));

            Assert.Equal(14, res);
        }

        [Fact]
        public async Task InvokeAsync_Cancellation_Throws()
        {
            var c = new FakeCatalog();
            c.Add("Wait", (Func<CancellationToken, Task>)(async ct => { await Task.Delay(5000, ct); }));

            var r = new ToolRuntime(c);
            var ct = new CancellationTokenSource();
            ct.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                r.InvokeAsync(Call("Wait"), ct.Token));
        }

        [Fact]
        public async Task InvokeAsync_MissingTool_ThrowsExecutionException()
        {
            var c = new FakeCatalog();
            var r = new ToolRuntime(c);

            var ex = await Assert.ThrowsAsync<ToolExecutionException>(() =>
                r.InvokeAsync(Call("Unknown", 1)));

            Assert.Equal("Unknown", ex.ToolName);
        }

        [Fact]
        public async Task InvokeAsync_ToolThrows_ThrowsWrappedExecutionException()
        {
            var c = new FakeCatalog();
            c.Add("Boom", (Func<int>)(() => throw new Exception("err")));

            var r = new ToolRuntime(c);

            var ex = await Assert.ThrowsAsync<ToolExecutionException>(() =>
                r.InvokeAsync(Call("Boom")));

            Assert.Contains("Failed to invoke tool", ex.Message);
        }

        [Fact]
        public async Task InvokeAsync_CancTokenInjectedCorrectly()
        {
            var catalog = new FakeCatalog();

            catalog.Add("Test", Injected);

            var runtime = new ToolRuntime(catalog);

            // PARAMETERS MUST BE NULL — CT is injected
            var call = new ToolCall(
                Guid.NewGuid().ToString(),
                "Test",
                JObject.Parse("{\"x\":4}"),   // JSON arguments
                new object[] { 4 },                         // MUST BE NULL
                null
            );

            var result = await runtime.InvokeAsync(call, new CancellationTokenSource().Token);

            Assert.Equal(5, result);
        }
        public int Injected(int x, CancellationToken ct)
        {
            if (!ct.CanBeCanceled)
                throw new Exception("CT missing");

            return x + 1;
        }

        [Fact]
        public async Task HandleToolCallAsync_TextOnlyMessage_ReturnsNullResult()
        {
            var c = new FakeCatalog();
            var r = new ToolRuntime(c);

            var call = new ToolCall("id", "", new JObject(), null, "assistant message");
            var res = await r.HandleToolCallAsync(call);

            Assert.Null(res.Result);
            Assert.Equal("assistant message", call.Message);
        }

        [Fact]
        public async Task HandleToolCallAsync_ExecutionError_ReturnsExceptionAsResult()
        {
            var c = new FakeCatalog();
            c.Add("Fail", (Func<int>)(() => throw new Exception("bad")));

            var r = new ToolRuntime(c);
            var res = await r.HandleToolCallAsync(Call("Fail"));

            Assert.IsType<ToolExecutionException>(res.Result);
        }

        [Fact]
        public async Task HandleToolCallAsync_SuccessWrapsResult()
        {
            var c = new FakeCatalog();
            c.Add("Mul", (Func<int, int, int>)((a, b) => a * b));

            var r = new ToolRuntime(c);
            var res = await r.HandleToolCallAsync(Call("Mul", 3, 4));

            Assert.Equal(12, res.Result);
        }

        [Fact]
        public async Task InvokeAsync_ParameterCountMismatch_ThrowsExecutionException()
        {
            var c = new FakeCatalog();
            c.Add("Add", (Func<int, int, int>)((a, b) => a + b));

            var r = new ToolRuntime(c);

            var ex = await Assert.ThrowsAsync<ToolExecutionException>(() =>
                r.InvokeAsync(Call("Add", 10))); // only one param, expected two

            Assert.Equal("Add", ex.ToolName);
        }

        [Fact]
        public async Task InvokeAsync_TaskOfVoidWithExtraArgs_ThrowsExecutionException()
        {
            var c = new FakeCatalog();
            c.Add("Async", (Func<Task>)(async () => await Task.Delay(1)));

            var r = new ToolRuntime(c);

            await Assert.ThrowsAsync<ToolExecutionException>(() =>
                r.InvokeAsync(Call("Async", 1))); // extra arg invalid
        }

        [Fact]
        public async Task InvokeAsync_Required_Optional_CT_Flow_Works()
        {
            var catalog = new FakeCatalog();

            // signature: int X(int a, int b = 9, CancellationToken ct)
            int X(int a, int b, CancellationToken ct)
            {
                if (!ct.CanBeCanceled)
                    throw new Exception("CT not injected");
                return a + b;
            }

            catalog.Add("X", (Func<int, int, CancellationToken, int>)X);

            var runtime = new ToolRuntime(catalog);

            // simulate ParseToolParams output (parser stripped CT)
            var call = new ToolCall(
                id: Guid.NewGuid().ToString(),
                name: "X",
                arguments: JObject.Parse("{\"a\":4,\"b\":6}"),
                parameters: new object[] { 4, 6 },   // ONLY JSON params
                message: null
            );

            var cts = new CancellationTokenSource();
            var result = await runtime.InvokeAsync(call, cts.Token);

            Assert.Equal(10, result);   // 4 + 6
        }
        [Fact]
        public async Task InvokeAsync_MixedParams_OptionalNullable_CT_AllCorrect()
        {
            var catalog = new FakeCatalog();

            [Tool]
            static int M(int a, string? b, int? c, CancellationToken ct = default)
            {
                if (!ct.CanBeCanceled) throw new Exception("CT missing");
                return a + (b?.Length ?? 0) + (c ?? 0);
            }

            catalog.Add("M", (Func<int, string?, int?, CancellationToken, int>)M);

            var parserCatalog = new FakeCatalog();
            parserCatalog.Add("M", (Func<int, string?, int?, CancellationToken, int>)M);

            var parser = new ToolCallParser(parserCatalog);

            var args = JObject.Parse("{\"a\":1, \"b\":\"xx\"}");
            var parsed = parser.ParseToolParams("M", args); // c omitted → null, CT stripped

            var call = new ToolCall("id", "M", args, parsed, null);
            var runtime = new ToolRuntime(catalog);

            var res = await runtime.InvokeAsync(call, new CancellationTokenSource().Token);

            Assert.Equal(1 + 2 + 0, res);
        }

        [Fact]
        public async Task InvokeAsync_RequiredOptionalNullable_AllThreeAligned()
        {
            var catalog = new FakeCatalog();

            [Tool]
            static int T(int req, int? opt, int opt2 = 5) => req + (opt ?? 0) + opt2;

            catalog.Add("T", (Func<int, int?, int, int>)T);

            var parserCatalog = new FakeCatalog();
            parserCatalog.Add("T", (Func<int, int?, int, int>)T);

            var parser = new ToolCallParser(parserCatalog);

            var args = JObject.Parse("{\"req\":10}"); // opt=null, opt2=default

            var parsed = parser.ParseToolParams("T", args);

            var call = new ToolCall("id", "T", args, parsed, null);
            var runtime = new ToolRuntime(catalog);

            var res = await runtime.InvokeAsync(call);

            Assert.Equal(10 + 0 + 5, res);
        }
    }
}
