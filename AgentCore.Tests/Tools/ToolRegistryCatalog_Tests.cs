using AgentCore.Tools;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading;
using Xunit;

namespace AgentCore.Tests.Tools
{
    public sealed class ToolRegistryCatalog_Tests
    {
        class StaticTools
        {
            [Tool("Adds two numbers")]
            public static int Add(int a, int b) => a + b;

            [Tool]
            public static void NoParams() { }

            [Tool]
            public static int HasCT(int x, CancellationToken ct) => x + 1;

            [Tool]
            public static void Bad(ref int x) { }
        }

        class InstanceTools
        {
            [Tool]
            public int Echo(int x) => x;

            [Tool]
            public string Combine(string a, string b) => a + b;
        }

        class EdgeCaseTools
        {
            [Tool] public static void RefParam(ref int x) { }
            [Tool] public static void OutParam(out int x) { x = 0; }
            [Tool] public unsafe static void Pointer(int* x) { }
            [Tool] public static void GenericMethod<T>(T input) { }
            [Tool] public static System.Collections.Generic.List<T> GenericReturn<T>() => null;
            [Tool] public static int Many(int a, string b, double c, bool d, long e) => 1;

            public class ComplexObj
            {
                public int X { get; set; }
                public string Y { get; set; }
            }

            [Tool] public static int ComplexArg(ComplexObj o) => 1;
        }

        // ───────────────────────────────────────────────────────────

        [Fact]
        public void Register_AddsDelegate_AsTool_WithNamespacedName()
        {
            var reg = new ToolRegistryCatalog();
            reg.Register((Func<int, int, int>)StaticTools.Add);

            Assert.True(reg.Contains("StaticTools.Add"));

            var t = reg.Get("StaticTools.Add");
            Assert.NotNull(t);
            Assert.Equal("Adds two numbers", t.Description);
        }

        [Fact]
        public void Register_Excludes_CancellationToken_FromSchema()
        {
            var reg = new ToolRegistryCatalog();
            reg.Register((Func<int, CancellationToken, int>)StaticTools.HasCT);

            var tool = reg.Get("StaticTools.HasCT");
            Assert.NotNull(tool);

            var props = (JObject)tool.ParametersSchema["properties"];
            Assert.True(props.ContainsKey("x"));
            Assert.False(props.ContainsKey("ct"));
        }

        [Fact]
        public void RegisterAll_Static_RegistersOnlyAnnotated()
        {
            var reg = new ToolRegistryCatalog();
            reg.RegisterAll<StaticTools>();

            Assert.True(reg.Contains("StaticTools.Add"));
            Assert.True(reg.Contains("StaticTools.NoParams"));
            Assert.True(reg.Contains("StaticTools.HasCT"));
            Assert.False(reg.Contains("StaticTools.Bad"));
        }

        [Fact]
        public void RegisterAll_Instance_RegistersOnlyAnnotated()
        {
            var reg = new ToolRegistryCatalog();
            reg.RegisterAll(new InstanceTools());

            Assert.True(reg.Contains("InstanceTools.Echo"));
            Assert.True(reg.Contains("InstanceTools.Combine"));
        }

        [Fact]
        public void Duplicate_ToolName_Throws()
        {
            var reg = new ToolRegistryCatalog();

            reg.Register((Func<int, int, int>)StaticTools.Add);

            Assert.Throws<InvalidOperationException>(() =>
                reg.Register((Func<int, int, int>)StaticTools.Add));
        }

        [Fact]
        public void CaseInsensitive_Lookup_Works()
        {
            var reg = new ToolRegistryCatalog();
            reg.Register((Func<int, int, int>)StaticTools.Add);

            Assert.True(reg.Contains("statictools.add"));
            Assert.NotNull(reg.Get("STATICTOOLS.ADD"));
        }

        [Fact]
        public void EdgeCases_IncompatibleMethods_AreSkipped()
        {
            var reg = new ToolRegistryCatalog();
            reg.RegisterAll<EdgeCaseTools>();

            Assert.False(reg.Contains("EdgeCaseTools.RefParam"));
            Assert.False(reg.Contains("EdgeCaseTools.OutParam"));
            Assert.False(reg.Contains("EdgeCaseTools.Pointer"));
            Assert.False(reg.Contains("EdgeCaseTools.GenericMethod"));
            Assert.False(reg.Contains("EdgeCaseTools.GenericReturn"));
        }

        [Fact]
        public void EdgeCases_ComplexAndManyParams_AreAccepted()
        {
            var reg = new ToolRegistryCatalog();
            reg.RegisterAll<EdgeCaseTools>();

            Assert.True(reg.Contains("EdgeCaseTools.ComplexArg"));
            Assert.True(reg.Contains("EdgeCaseTools.Many"));
        }

        [Fact]
        public void Required_Parameters_AreCorrect()
        {
            var reg = new ToolRegistryCatalog();
            reg.Register((Func<int, int, int>)StaticTools.Add);

            var t = reg.Get("StaticTools.Add");
            var req = (JArray)t.ParametersSchema["required"];

            Assert.Contains("a", req);
            Assert.Contains("b", req);
        }

        public class Obj
        {
            public string X { get; set; }
            public int? Y { get; set; }
        }

        [Tool]
        private static int T(Obj o, CancellationToken ct) => 0;

        [Fact]
        public void Complex_Object_Param_IsAccepted()
        {
            var reg = new ToolRegistryCatalog();
            reg.Register((Func<Obj, CancellationToken, int>)T);

            Assert.True(reg.Contains($"{nameof(ToolRegistryCatalog_Tests)}.T"));

            var t = reg.Get($"{nameof(ToolRegistryCatalog_Tests)}.T");
            var props = (JObject)t.ParametersSchema["properties"];

            Assert.True(props.ContainsKey("o"));
        }
    }
}
