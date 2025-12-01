using AgentCore.Json;
using AgentCore.Tools;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentCore.Tests.Tools
{
    public class ToolCallParserTests
    {
        // simple fake catalog
        class FakeToolCatalog : IToolCatalog
        {
            private readonly Dictionary<string, Tool> _tools = new();

            public IReadOnlyList<Tool> RegisteredTools => new List<Tool>(_tools.Values);
            public bool Contains(string name) => _tools.ContainsKey(name);
            public Tool Get(string name) => _tools.TryGetValue(name, out var t) ? t : null;
            public void Add(Tool t) => _tools[t.Name] = t;
        }

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
            var catalog = new FakeToolCatalog();

            catalog.Add(new Tool
            {
                Name = "Add",
                Function = (Func<int, int, int>)Tools.Add,
                ParametersSchema = typeof(int).GetSchemaForType()
            });

            catalog.Add(new Tool
            {
                Name = "Calc",
                Function = (Func<Tools.InputObj, int>)Tools.Calc,
                ParametersSchema = typeof(Tools.InputObj).GetSchemaForType()
            });

            catalog.Add(new Tool
            {
                Name = "Defaulted",
                Function = (Func<int, int>)Tools.Defaulted,
                ParametersSchema = typeof(int).GetSchemaForType()
            });

            return new ToolCallParser(catalog);
        }

        // ───────────────────────────────────────────────
        // INLINE JSON EXTRACTION TESTS
        // ───────────────────────────────────────────────

        [Fact]
        public void ExtractInlineToolCall_ReturnsCall_WhenValid()
        {
            var p = MakeParser();
            var txt = "prefix { \"name\": \"Add\", \"arguments\": {\"a\":1, \"b\":2} }";

            var res = p.ExtractInlineToolCall(txt);

            Assert.NotNull(res.Call);
            Assert.Equal("Add", res.Call.Name);
            Assert.Equal("prefix", res.AssistantMessage);
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
                "a {\"name\":\"Add\",\"arguments\":{\"a\":1,\"b\":2}} " +
                "b {\"name\":\"Add\",\"arguments\":{\"a\":10,\"b\":20}}";

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
            var txt = "{ \"name\": \"Add\", \"arguments\": {\"a\": 1";

            var res = p.ExtractInlineToolCall(txt);

            Assert.Null(res.Call);
            Assert.Equal(txt.Trim(), res.AssistantMessage);
        }

        [Fact]
        public void ExtractInlineToolCall_MultipleJson_FirstOnly()
        {
            var p = MakeParser();
            var txt =
                "first {\"name\":\"Add\",\"arguments\":{\"a\":1,\"b\":2}} " +
                "second {\"name\":\"Add\",\"arguments\":{\"a\":9,\"b\":9}}";

            var res = p.ExtractInlineToolCall(txt);

            Assert.NotNull(res.Call);
            Assert.Equal(1, (int)res.Call.Arguments["a"]);
        }

        // ───────────────────────────────────────────────
        // EXTENDED WEIRD INLINE CASES
        // ───────────────────────────────────────────────

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
            var txt = "text { \"name\": \"Add\", \"arguments\": {\"a\": 1, \"b\": 2} } more";

            var res = p.ExtractInlineToolCall(txt);

            Assert.NotNull(res.Call);
            Assert.Equal("text", res.AssistantMessage);
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

        [Fact]
        public void ExtractInlineToolCall_ToolCallInsideSentence_ShouldExtractPrefixCorrectly()
        {
            var p = MakeParser();
            var txt = "The model says: {\"name\":\"Add\",\"arguments\":{\"a\":1,\"b\":2}} end.";

            var res = p.ExtractInlineToolCall(txt);

            Assert.NotNull(res.Call);
            Assert.Equal("The model says:", res.AssistantMessage);
        }

        // ───────────────────────────────────────────────
        // PARAM PARSING TESTS
        // ───────────────────────────────────────────────

        [Fact]
        public void ParseToolParams_SimpleValues_Works()
        {
            var p = MakeParser();
            var args = JObject.Parse("{\"a\": 3, \"b\": 4}");

            var res = p.ParseToolParams("Add", args);

            Assert.Equal(3, (int)res[0]);
            Assert.Equal(4, (int)res[1]);
        }

        [Fact]
        public void ParseToolParams_Complex_WrappedCorrectly()
        {
            var p = MakeParser();
            var args = JObject.Parse("{\"x\":5,\"y\":10}");

            var res = p.ParseToolParams("Calc", args);

            var obj = Assert.IsType<Tools.InputObj>(res[0]);
            Assert.Equal(5, obj.x);
            Assert.Equal(10, obj.y);
        }

        [Fact]
        public void ParseToolParams_DefaultValue_IsUsed()
        {
            var p = MakeParser();
            var args = JObject.Parse("{}");

            var res = p.ParseToolParams("Defaulted", args);

            Assert.Equal(10, (int)res[0]);
        }

        [Fact]
        public void ParseToolParams_MissingRequired_Throws()
        {
            var p = MakeParser();
            var args = JObject.Parse("{\"a\":1}");

            Assert.Throws<ToolValidationException>(() =>
                p.ParseToolParams("Add", args));
        }

        [Fact]
        public void ParseToolParams_WrongType_ThrowsAggregate()
        {
            var p = MakeParser();
            var args = JObject.Parse("{\"a\":\"wrong\",\"b\":\"bad\"}");

            Assert.Throws<ToolValidationAggregateException>(() =>
                p.ParseToolParams("Add", args));
        }

        // Extra: complex type wrong structure
        [Fact]
        public void ParseToolParams_ComplexType_InvalidStructure_Throws()
        {
            var p = MakeParser();
            var args = JObject.Parse("{\"x\":\"bad\",\"y\": {\"nested\": true}}");

            Assert.Throws<ToolValidationAggregateException>(() =>
                p.ParseToolParams("Calc", args));
        }

        // Extra: nulls & missing inside complex
        [Fact]
        public void ParseToolParams_ComplexType_NullMembers_Throws()
        {
            var p = MakeParser();
            var args = JObject.Parse("{\"x\":null, \"y\":null}");

            Assert.Throws<ToolValidationAggregateException>(() =>
                p.ParseToolParams("Calc", args));
        }
    }
}
