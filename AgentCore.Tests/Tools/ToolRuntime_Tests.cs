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
        [Fact]
        public async Task Runtime_InvokesMethod()
        {
            var cat = new FakeCatalog();
            var rt = new ToolRuntime(cat);

            var call = new ToolCall("1", "Add", JObject.Parse("{\"a\":2,\"b\":3}"), new object[] { 2, 3 });
            var result = await rt.HandleToolCallAsync(call);

            Assert.Equal(5, result.Result);
        }

        class FakeCatalog : IToolCatalog
        {
            public IReadOnlyList<Tool> RegisteredTools => new List<Tool> {
            new Tool{
                Name="Add",
                ParametersSchema = JObject.Parse("{\"properties\":{\"a\":{},\"b\":{}},\"required\":[\"a\",\"b\"]}"),
                Function = new System.Func<int,int,int>((a,b)=>a+b)
            }
        };
            public bool Contains(string x) => x == "Add";
            public Tool Get(string x) => RegisteredTools[0];
        }
    }
}
