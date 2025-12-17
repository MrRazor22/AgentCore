using AgentCore.Tools;
using Newtonsoft.Json.Linq;
using System;
using Xunit;

namespace AgentCore.Tests.Tools
{
    public sealed class ToolCallParser_Tests
    {
        private readonly ToolCallParser _parser;

        // helper container so declaring type is stable
        private static class TestTools
        {
            public static int Sum(int a, int b) => a + b;
            public static string Echo(string text) => text;
            public static int Complex(ComplexArg arg) => arg.Value;
        }

        public ToolCallParser_Tests()
        {
            var catalog = new ToolRegistryCatalog();
            catalog.Register(
                (Func<int, int, int>)TestTools.Sum,
                (Func<string, string>)TestTools.Echo,
                (Func<ComplexArg, int>)TestTools.Complex
            );

            _parser = new ToolCallParser(catalog);
        }

        [Fact]
        public void TryMatch_NoJson_ReturnsNull()
        {
            Assert.Null(_parser.TryMatch("hello world"));
        }

        [Fact]
        public void TryMatch_ValidTool_MatchesFirstOnly()
        {
            var text = @"before {""name"":""TestTools.Sum"",""arguments"":{""a"":1,""b"":2}}
                         mid {""name"":""TestTools.Echo"",""arguments"":{""text"":""x""}} after";

            var match = _parser.TryMatch(text);

            Assert.NotNull(match);
            Assert.Equal("TestTools.Sum", match!.Call.Name);
            Assert.Contains("before", text[..match.Start]);
            Assert.Contains("after", text[match.End..]);
        }

        [Fact]
        public void TryMatch_JsonLikeButNotTool_Ignored()
        {
            Assert.Null(_parser.TryMatch(@"{""title"":""TestTools.Sum"",""arguments"":{""a"":1}}"));
        }

        [Fact]
        public void TryMatch_InvalidJson_Ignored()
        {
            Assert.Null(_parser.TryMatch(@"{""name"":""TestTools.Sum"",""arguments"":{""a"":1,}"));
        }

        [Fact]
        public void ParseToolParams_SimpleParams_Work()
        {
            var args = JObject.Parse(@"{""a"":1,""b"":2}");
            var values = _parser.ParseToolParams("TestTools.Sum", args);

            Assert.Equal(new object[] { 1, 2 }, values);
        }

        [Fact]
        public void ParseToolParams_MissingRequired_Throws()
        {
            var args = JObject.Parse(@"{""a"":1}");

            var ex = Assert.Throws<ToolValidationException>(() =>
                _parser.ParseToolParams("TestTools.Sum", args));

            Assert.Equal("b", ex.ParamName);
        }

        [Fact]
        public void ParseToolParams_WrongTypes_ThrowsAggregate()
        {
            var args = JObject.Parse(@"{""a"":""x"",""b"":""y""}");

            Assert.Throws<ToolValidationAggregateException>(() =>
                _parser.ParseToolParams("TestTools.Sum", args));
        }

        [Fact]
        public void ParseToolParams_NullArguments_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                _parser.ParseToolParams("TestTools.Sum", null!));
        }

        [Fact]
        public void ParseToolParams_ReferenceWrappedParam_Works()
        {
            var args = JObject.Parse(@"{""value"":5}");
            var values = _parser.ParseToolParams("TestTools.Complex", args);

            Assert.Equal(5, values[0]);
        }

        [Fact]
        public void ParseToolParams_NestedJson_Works()
        {
            var args = JObject.Parse(@"{""arg"":{""value"":10}}");
            var values = _parser.ParseToolParams("TestTools.Complex", args);

            Assert.Equal(10, ((ComplexArg)values[0]).Value);
        }

        [Fact]
        public void ParseToolParams_UnregisteredTool_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                _parser.ParseToolParams("Missing.Tool", JObject.Parse("{}")));
        }

        private sealed class ComplexArg
        {
            public int Value { get; set; }
        }
    }
}
