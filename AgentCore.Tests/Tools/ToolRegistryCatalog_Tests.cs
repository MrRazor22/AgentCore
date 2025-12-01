using AgentCore.Tools;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading;
using Xunit;

namespace AgentCore.Tests.Tools
{
    public class ToolRegistryCatalog_Tests
    {
        // ───────────────────────────────────────────────────────────
        // Helper classes for testing
        // ───────────────────────────────────────────────────────────

        class StaticTools
        {
            [Tool("Adds two numbers")]
            public static int Add(int a, int b) => a + b;

            [Tool]
            public static void NoParams() { }

            [Tool]
            public static int HasCT(int x, CancellationToken ct) => x + 1;

            [Tool]
            public static void Bad(ref int x) { }        // incompatible
        }

        class InstanceTools
        {
            [Tool]
            public int Echo(int x) => x;

            [Tool]
            public string Combine(string a, string b) => a + b;

            public int Bad(string a, int b, int c, int d, string e) => 0; // no [Tool] → ignored
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
        // Basic Tests
        // ───────────────────────────────────────────────────────────

        [Fact]
        public void Register_AddsDelegate_AsTool()
        {
            var reg = new ToolRegistryCatalog();
            reg.Register((Func<int, int, int>)StaticTools.Add);

            Assert.True(reg.Contains("Add"));
            var t = reg.Get("Add");

            Assert.NotNull(t);
            Assert.Equal("Adds two numbers", t.Description);
        }

        [Fact]
        public void Register_CreatesCorrectSchema_ExcludesCancellationToken()
        {
            var reg = new ToolRegistryCatalog();
            reg.Register((Func<int, CancellationToken, int>)StaticTools.HasCT);

            var tool = reg.Get("HasCT");
            Assert.NotNull(tool);

            var props = (JObject)tool.ParametersSchema["properties"];
            Assert.True(props.ContainsKey("x"));
            Assert.False(props.ContainsKey("ct"));
        }

        [Fact]
        public void Register_ThrowsOnNullDelegates_ArrayNull()
        {
            var reg = new ToolRegistryCatalog();
            Assert.Throws<ArgumentNullException>(() => reg.Register(null));
        }

        [Fact]
        public void Register_ThrowsOnNullDelegates_EntryNull()
        {
            var reg = new ToolRegistryCatalog();
            Assert.Throws<ArgumentNullException>(() => reg.Register(null!));
        }

        // ───────────────────────────────────────────────────────────
        // Static registration
        // ───────────────────────────────────────────────────────────

        [Fact]
        public void RegisterAll_Static_RegistersOnlyAnnotated()
        {
            var reg = new ToolRegistryCatalog();
            reg.RegisterAll<StaticTools>();

            Assert.True(reg.Contains("Add"));
            Assert.True(reg.Contains("NoParams"));
            Assert.True(reg.Contains("HasCT"));
            Assert.False(reg.Contains("Bad"));
        }

        [Fact]
        public void RegisterAll_Static_Skips_IncompatibleMethods()
        {
            var reg = new ToolRegistryCatalog();
            reg.RegisterAll<StaticTools>();

            Assert.False(reg.Contains("Bad")); // ref param → skipped
        }

        // ───────────────────────────────────────────────────────────
        // Instance registration
        // ───────────────────────────────────────────────────────────

        [Fact]
        public void RegisterAll_Instance_RegistersOnlyAnnotated()
        {
            var inst = new InstanceTools();
            var reg = new ToolRegistryCatalog();
            reg.RegisterAll(inst);

            Assert.True(reg.Contains("Echo"));
            Assert.True(reg.Contains("Combine"));
            Assert.False(reg.Contains("Bad")); // not annotated
        }

        [Fact]
        public void RegisterAll_Instance_SkipsIncompatible()
        {
            var inst = new InstanceTools();
            var reg = new ToolRegistryCatalog();
            reg.RegisterAll(inst);

            var names = reg.RegisteredTools.Select(t => t.Name).ToList();
            Assert.DoesNotContain("Bad", names);
        }

        // ───────────────────────────────────────────────────────────
        // Case-insensitivity
        // ───────────────────────────────────────────────────────────

        [Fact]
        public void Contains_IsCaseInsensitive()
        {
            var reg = new ToolRegistryCatalog();
            reg.Register((Func<int, int, int>)StaticTools.Add);

            Assert.True(reg.Contains("add"));
            Assert.True(reg.Contains("ADD"));
        }

        [Fact]
        public void Get_IsCaseInsensitive()
        {
            var reg = new ToolRegistryCatalog();
            reg.Register((Func<int, int, int>)StaticTools.Add);

            Assert.NotNull(reg.Get("add"));
            Assert.NotNull(reg.Get("ADD"));
        }

        // ───────────────────────────────────────────────────────────
        // Schema correctness
        // ───────────────────────────────────────────────────────────

        [Fact]
        public void Schema_HasRequiredAndOptionalProperly()
        {
            var reg = new ToolRegistryCatalog();
            reg.Register((Func<int, int, int>)StaticTools.Add);

            var t = reg.Get("Add");
            var req = (JArray)t.ParametersSchema["required"];

            Assert.Contains("a", req);
            Assert.Contains("b", req);
        }

        // ───────────────────────────────────────────────────────────
        // Edge-case compatibility (ref/out/pointer/generic/etc.)
        // ───────────────────────────────────────────────────────────

        [Fact]
        public void EdgeCase_RefParam_IsSkipped()
        {
            var reg = new ToolRegistryCatalog();
            reg.RegisterAll<EdgeCaseTools>();

            Assert.False(reg.Contains("RefParam"));
        }

        [Fact]
        public void EdgeCase_OutParam_IsSkipped()
        {
            var reg = new ToolRegistryCatalog();
            reg.RegisterAll<EdgeCaseTools>();

            Assert.False(reg.Contains("OutParam"));
        }

        [Fact]
        public unsafe void EdgeCase_Pointer_IsSkipped()
        {
            var reg = new ToolRegistryCatalog();
            reg.RegisterAll<EdgeCaseTools>();

            Assert.False(reg.Contains("Pointer"));
        }

        [Fact]
        public void EdgeCase_GenericMethod_IsSkipped()
        {
            var reg = new ToolRegistryCatalog();
            reg.RegisterAll<EdgeCaseTools>();

            Assert.False(reg.Contains("GenericMethod"));
        }

        [Fact]
        public void EdgeCase_GenericReturn_IsSkipped()
        {
            var reg = new ToolRegistryCatalog();
            reg.RegisterAll<EdgeCaseTools>();

            Assert.False(reg.Contains("GenericReturn"));
        }

        [Fact]
        public void EdgeCase_ComplexArg_IsAccepted()
        {
            var reg = new ToolRegistryCatalog();
            reg.RegisterAll<EdgeCaseTools>();

            Assert.True(reg.Contains("ComplexArg"));
        }

        [Fact]
        public void EdgeCase_ManyParams_IsAccepted()
        {
            var reg = new ToolRegistryCatalog();
            reg.RegisterAll<EdgeCaseTools>();

            Assert.True(reg.Contains("Many"));
        }
    }
}
