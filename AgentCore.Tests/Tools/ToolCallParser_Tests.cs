using AgentCore.Json;
using AgentCore.Tools;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static AgentCore.Tests.Tools.ToolCallParserTests;

namespace AgentCore.Tests.Tools
{
    public class ToolCallParserTests
    {
        class Tools
        {
            [Tool]
            public static int Add(int a, int b) => a + b;

            public class InputObj
            {
                public int x { get; set; }
                public int y { get; set; }
            }

            [Tool]
            public static int Calc(InputObj obj) => obj.x + obj.y;

            [Tool]
            public static int Defaulted(int x = 10) => x;
        }

        ToolCallParser MakeParser()
        {
            var catalog = new ToolRegistryCatalog();

            // This picks up all static [Tool] methods in Tools
            catalog.RegisterAll<Tools>();

            return new ToolCallParser(catalog);
        }

        [Fact]
        public void ExtractInlineToolCall_ReturnsCall_WhenValid()
        {
            var p = MakeParser();
            var txt = "prefix ``{ \"name\": \"Add\", \"arguments\": {\"a\":1, \"b\":2} }``";

            var res = p.ExtractInlineToolCall(txt);

            Assert.NotNull(res.Call);
            Assert.Equal("Add", res.Call.Name);
            Assert.Equal("prefix ", res.AssistantMessage);
        }

        [Fact]
        public void ExtractInlineToolCall_NoCall_ReturnsFullText()
        {
            var p = MakeParser();
            var res = p.ExtractInlineToolCall("just text");

            Assert.Null(res.Call);
            Assert.Equal("just text", res.AssistantMessage);
        }

        [Fact]
        public void ExtractInlineToolCall_OnlyFirstCallReturned()
        {
            var p = MakeParser();
            var txt =
                "a <tool_call>{\"name\":\"Add\",\"arguments\":{\"a\":1,\"b\":2}}</tool_call>" +
                "b <TOOLCALL>{\"name\":\"Add\",\"arguments\":{\"a\":10,\"b\":20}}</TOOLCALL>";

            var res = p.ExtractInlineToolCall(txt);

            Assert.NotNull(res.Call);
            Assert.Equal(1, (int)res.Call.Arguments["a"]);
        }

        [Fact]
        public void ExtractInlineToolCall_InvalidName_Ignored()
        {
            var p = MakeParser();
            var txt = "{ \"name\": \"UnknownTool\", \"arguments\": {\"a\":1} }";

            var res = p.ExtractInlineToolCall(txt);

            Assert.Null(res.Call);
            Assert.Equal(txt, res.AssistantMessage);
        }

        [Fact]
        public void ExtractInlineToolCall_PartialJson_NoCall()
        {
            var p = MakeParser();
            var txt = "<tool_call>{ \"name\": \"Add\", \"arguments\": {\"a\": 1";

            var res = p.ExtractInlineToolCall(txt);

            Assert.Null(res.Call);
            Assert.Equal(txt.Trim(), res.AssistantMessage);
        }

        [Fact]
        public void ExtractInlineToolCall_MultilineJson_Works()
        {
            var p = MakeParser();
            var txt = @"prefix
                        {
                           ""name"": ""Add"",
                           ""arguments"": { ""a"": 10, ""b"": 20 }
                        }";

            var res = p.ExtractInlineToolCall(txt);

            Assert.NotNull(res.Call);
            Assert.Equal("prefix", res.AssistantMessage.Trim());
        }

        [Fact]
        public void ExtractInlineToolCall_EscapedJson_StillFound()
        {
            var p = MakeParser();
            var txt = "tool { \"name\": \"Add\", \"arguments\": {\"a\": 1, \"b\": 2} } more";

            var res = p.ExtractInlineToolCall(txt);

            Assert.NotNull(res.Call);
            Assert.Equal("tool ", res.AssistantMessage);
        }

        [Fact]
        public void ExtractInlineToolCall_NoPrefix_PrefixEmptyString()
        {
            var p = MakeParser();
            var txt = "{\"name\":\"Add\", \"arguments\":{\"a\":1,\"b\":2}}";

            var res = p.ExtractInlineToolCall(txt);

            Assert.NotNull(res.Call);
            Assert.Equal("", res.AssistantMessage);
        }

        public class C1 { public int A { get; set; } }
        public class C2 { public int X { get; set; } }
        public class C3 { public int X { get; set; } }

        [Fact]
        public void ParseToolParams_ToolNotFound_Throws()
        {
            var catalog = new ToolRegistryCatalog();
            var p = new ToolCallParser(catalog);

            Assert.Throws<InvalidOperationException>(() =>
                p.ParseToolParams("Ghost", new JObject()));
        }

        [Fact]
        public void ParseToolParams_NullArguments_Throws()
        {
            int F(int x) => x;

            var catalog = new ToolRegistryCatalog();
            catalog.Register(F);
            var p = new ToolCallParser(catalog);

            Assert.Throws<ArgumentException>(() =>
                p.ParseToolParams("F", null));
        }

        [Fact]
        public void ParseToolParams_SingleComplexParam_AutoWraps()
        {
            int F(C1 c) => c.A;

            var catalog = new ToolRegistryCatalog();
            catalog.Register(F);
            var p = new ToolCallParser(catalog);

            var args = JObject.Parse("{\"A\":5}");
            var res = p.ParseToolParams("F", args);

            Assert.Equal(5, ((C1)res[0]).A);
        }

        [Fact]
        public void ParseToolParams_MissingRequired_Throws()
        {
            int Add(int a, int b) => a + b;

            var catalog = new ToolRegistryCatalog();
            catalog.Register(Add);
            var p = new ToolCallParser(catalog);

            var args = JObject.Parse("{\"a\":1}");
            Assert.Throws<ToolValidationException>(() =>
                p.ParseToolParams("Add", args));
        }

        [Fact]
        public void ParseToolParams_Optional_UsesDefault()
        {
            int F(int a = 7) => a;

            var catalog = new ToolRegistryCatalog();
            catalog.Register(F);
            var p = new ToolCallParser(catalog);

            var res = p.ParseToolParams("F", JObject.Parse("{}"));
            Assert.Equal(7, (int)res[0]);
        }

        [Fact]
        public void ParseToolParams_IgnoresCancellationToken()
        {
            int F(int a, CancellationToken ct) => a;

            var catalog = new ToolRegistryCatalog();
            catalog.Register(F);
            var p = new ToolCallParser(catalog);

            var res = p.ParseToolParams("F", JObject.Parse("{\"a\":3}"));
            Assert.Single(res);
            Assert.Equal(3, (int)res[0]);
        }

        [Fact]
        public void ParseToolParams_ValidationFails_ThrowsAggregate()
        {
            int F(int a) => a;

            var catalog = new ToolRegistryCatalog();
            catalog.Register(F);
            var p = new ToolCallParser(catalog);

            var args = JObject.Parse("{\"a\":\"bad\"}");
            Assert.Throws<ToolValidationAggregateException>(() =>
                p.ParseToolParams("F", args));
        }

        [Fact]
        public void ParseToolParams_ToObjectFails_Throws()
        {
            int F(C2 c) => 1;

            var catalog = new ToolRegistryCatalog();
            catalog.Register(F);
            var p = new ToolCallParser(catalog);

            var args = JObject.Parse("{\"c\": {\"X\": \"bad\" }}");
            Assert.Throws<ToolValidationException>(() =>
                p.ParseToolParams("F", args));
        }

        [Fact]
        public void ParseToolParams_ComplexObject_Works()
        {
            int F(C3 c) => c.X;

            var catalog = new ToolRegistryCatalog();
            catalog.Register(F);
            var p = new ToolCallParser(catalog);

            var args = JObject.Parse("{\"X\": 9}");
            var res = p.ParseToolParams("F", args);

            Assert.Equal(9, ((C3)res[0]).X);
        }

        [Fact]
        public void ParseToolParams_Nullable_Works()
        {
            int F(int? x) => x ?? -1;

            var catalog = new ToolRegistryCatalog();
            catalog.Register(F);
            var p = new ToolCallParser(catalog);

            var args = JObject.Parse("{\"x\": null}");
            var res = p.ParseToolParams("F", args);

            Assert.Null(res[0]);
        }

        [Fact]
        public void ParseToolParams_NullForNonNullable_Throws()
        {
            int F(int x) => x;

            var catalog = new ToolRegistryCatalog();
            catalog.Register(F);
            var p = new ToolCallParser(catalog);

            var args = JObject.Parse("{\"x\": null}");
            Assert.Throws<ToolValidationAggregateException>(() =>
                p.ParseToolParams("F", args));
        }
    }
}
