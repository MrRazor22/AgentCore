using AgentCore.Chat;
using Newtonsoft.Json.Linq;
using System.Linq;
using Xunit;

namespace AgentCore.Tests.Chat
{
    public class ConversationTests
    {
        [Fact]
        public void Add_FiresOnChat()
        {
            var c = new Conversation();
            AgentCore.Chat.Chat fired = null;

            c.OnChat += x => fired = x;

            c.Add(Role.User, "hi");

            Assert.NotNull(fired);
            Assert.Equal("hi", ((TextContent)fired.Content).Text);
        }

        [Fact]
        public void Clone_RespectsFilter()
        {
            var c = new Conversation();
            c.Add(Role.System, "s1");
            c.Add(Role.User, "u1");
            c.Add(Role.Assistant, "a1");

            var clone = c.Clone(ChatFilter.User);

            Assert.Single(clone);
            Assert.Equal(Role.User, clone[0].Role);
        }

        [Fact]
        public void Append_RespectsFilter()
        {
            var src = new Conversation();
            src.Add(Role.System, "s");
            src.Add(Role.User, "u");

            var dst = new Conversation();
            dst.Append(src, ChatFilter.User);

            Assert.Single(dst);
            Assert.Equal(Role.User, dst[0].Role);
        }

        [Fact]
        public void AddUser_AddsUserRoleMessage()
        {
            var c = new Conversation();
            c.AddUser("hello");

            Assert.Single(c);
            Assert.Equal(Role.User, c[0].Role);
        }

        [Fact]
        public void AddAssistantToolCall_AddsCorrectRole()
        {
            var c = new Conversation();
            var call = new ToolCall("id", "calc", JObject.Parse("{\"x\":1}"));

            c.AddAssistantToolCall(call);

            Assert.Equal(Role.Assistant, c[0].Role);
            Assert.IsType<ToolCall>(c[0].Content);
        }

        [Fact]
        public void AddToolResult_AddsToolRole()
        {
            var c = new Conversation();
            var call = new ToolCall("id", "calc", new JObject());
            var res = new ToolCallResult(call, 42);

            c.AddToolResult(res);

            Assert.Equal(Role.Tool, c[0].Role);
        }

        [Fact]
        public void ToJson_IncludesTextMessages()
        {
            var c = new Conversation();
            c.Add(Role.User, "hello");

            var json = c.ToJson();

            Assert.Contains("\"hello\"", json);
            Assert.Contains("\"role\": \"user\"", json.ToLower());
        }

        [Fact]
        public void ToJson_IncludesToolCall()
        {
            var c = new Conversation();
            var call = new ToolCall("id", "do", JObject.Parse("{\"x\":1}"));
            call.Message = "msg";

            c.AddAssistantToolCall(call);

            var json = c.ToJson();

            Assert.Contains("\"tool_calls\"", json);
            Assert.Contains("\"do\"", json);
        }

        [Fact]
        public void ToJson_IncludesToolResult()
        {
            var c = new Conversation();
            var call = new ToolCall("id", "calc", new JObject());
            var r = new ToolCallResult(call, "OK");

            c.AddToolResult(r);

            var json = c.ToJson();

            Assert.Contains("\"tool_call_id\": \"id\"", json);
            Assert.Contains("\"OK\"", json);
        }

        [Fact]
        public void ExistsIn_MatchesNormalizedArgs()
        {
            var c = new Conversation();
            var call1 = new ToolCall("id1", "calc", JObject.Parse("{\"b\":2,\"a\":1}"));
            var call2 = new ToolCall("id2", "calc", JObject.Parse("{\"a\":1,\"b\":2}"));

            c.AddAssistantToolCall(call1);

            Assert.True(call2.ExistsIn(c));
        }

        [Fact]
        public void GetLastToolCallResult_ReturnsLatest()
        {
            var c = new Conversation();
            var call = new ToolCall("id", "calc", JObject.Parse("{\"x\":1}"));

            c.AddToolResult(new ToolCallResult(call, 100));
            c.AddToolResult(new ToolCallResult(call, 200));

            Assert.Equal(200, c.GetLastToolCallResult(call));
        }

        [Fact]
        public void AppendToolCallResult_AddsCallAndResult()
        {
            var c = new Conversation();
            var call = new ToolCall("id", "foo", new JObject());
            var res = new ToolCallResult(call, "bar");

            c.AppendToolCallResult(res);

            Assert.Equal(2, c.Count);
            Assert.Equal(Role.Assistant, c[0].Role);
            Assert.Equal(Role.Tool, c[1].Role);
        }

        [Fact]
        public void IsLastAssistantMessageSame_TrueForMatch()
        {
            var c = new Conversation();
            c.Add(Role.Assistant, "hello");

            Assert.True(c.IsLastAssistantMessageSame("hello"));
            Assert.False(c.IsLastAssistantMessageSame("bye"));
        }

        [Fact]
        public void Filter_FiltersByRole()
        {
            var c = new Conversation();
            c.Add(Role.System, "s");
            c.Add(Role.User, "u");

            var filtered = c.Filter(ChatFilter.User).ToList();

            Assert.Single(filtered);
            Assert.Equal(Role.User, filtered[0].Role);
        }
    }
}
