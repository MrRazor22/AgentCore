using AgentCore.Tools;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentCore.Tests.Tools
{
    public class ToolCallParser_Tests
    {
        [Fact]
        public void ExtractInlineToolCall_DetectsTool()
        {
            var cat = new FakeCatalog();
            var p = new ToolCallParser(cat);

            var result = p.ExtractInlineToolCall(""" {"name":"Test","arguments":{"x":1}} """);

            Assert.NotNull(result.Call);
            Assert.Equal("Test", result.Call.Name);
        }

        class FakeCatalog : IToolCatalog
        {
            public IReadOnlyList<Tool> RegisteredTools => new List<Tool> { new Tool { Name = "Test", ParametersSchema = new JObject() } };
            public bool Contains(string x) => x == "Test";
            public Tool Get(string x) => RegisteredTools[0];
        }
    }

}
