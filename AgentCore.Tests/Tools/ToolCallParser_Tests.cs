using AgentCore.Tools;
using Newtonsoft.Json.Linq;

namespace AgentCore.Tests.Tools
{
    public sealed class ToolCallParser_Tests
    {
        private readonly ToolCallParser _parser;

        public ToolCallParser_Tests()
        {
            var catalog = new ToolRegistryCatalog();
            catalog.Register(
                (int a, int b) => a + b,
                (string text) => text,
                (ComplexArg arg) => arg.Value
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
            var text = @"before {""name"":""Sum"",""arguments"":{""a"":1,""b"":2}}
                     mid {""name"":""Echo"",""arguments"":{""text"":""x""}} after";

            var match = _parser.TryMatch(text);

            Assert.NotNull(match);
            Assert.Equal("Sum", match!.Call.Name);
            Assert.Contains("before", text[..match.Start]);
            Assert.Contains("after", text[match.End..]);
        }

        [Fact]
        public void TryMatch_JsonLikeButNotTool_Ignored()
        {
            Assert.Null(_parser.TryMatch(@"{""title"":""Sum"",""arguments"":{""a"":1}}"));
        }

        [Fact]
        public void TryMatch_InvalidJson_Ignored()
        {
            Assert.Null(_parser.TryMatch(@"{""name"":""Sum"",""arguments"":{""a"":1,}"));
        }

        [Fact]
        public void ParseToolParams_SimpleParams_Work()
        {
            var args = JObject.Parse(@"{""a"":1,""b"":2}");
            var values = _parser.ParseToolParams("Sum", args);
            Assert.Equal(new object[] { 1, 2 }, values);
        }

        [Fact]
        public void ParseToolParams_MissingRequired_Throws()
        {
            var args = JObject.Parse(@"{""a"":1}");
            var ex = Assert.Throws<ToolValidationException>(() =>
                _parser.ParseToolParams("Sum", args));
            Assert.Equal("b", ex.ParamName);
        }

        [Fact]
        public void ParseToolParams_WrongTypes_ThrowsAggregate()
        {
            var args = JObject.Parse(@"{""a"":""x"",""b"":""y""}");
            Assert.Throws<ToolValidationAggregateException>(() =>
                _parser.ParseToolParams("Sum", args));
        }

        [Fact]
        public void ParseToolParams_NullArguments_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                _parser.ParseToolParams("Sum", null!));
        }

        [Fact]
        public void ParseToolParams_ReferenceWrappedParam_Works()
        {
            var args = JObject.Parse(@"{""value"":5}");
            var values = _parser.ParseToolParams("Complex", args);
            Assert.Equal(5, values[0]);
        }

        [Fact]
        public void ParseToolParams_NestedJson_Works()
        {
            var args = JObject.Parse(@"{""arg"":{""value"":10}}");
            var values = _parser.ParseToolParams("Complex", args);
            Assert.Equal(10, ((ComplexArg)values[0]).Value);
        }

        [Fact]
        public void ParseToolParams_UnregisteredTool_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                _parser.ParseToolParams("Missing", JObject.Parse("{}")));
        }

        private sealed class ComplexArg
        {
            public int Value { get; set; }
        }
    }

}
