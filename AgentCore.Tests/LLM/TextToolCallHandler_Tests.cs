using AgentCore.Chat;
using AgentCore.LLM.Client;
using AgentCore.LLM.Handlers;
using AgentCore.LLM.Pipeline;
using AgentCore.Tokens;
using AgentCore.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentCore.Tests.LLM
{
    public class TextToolCallHandler_Tests
    { // -------- FAKES --------
        class FakeCatalog : IToolCatalog
        {
            public string Name = "Add";
            public IReadOnlyList<Tool> RegisteredTools =>
                new List<Tool> { new Tool { Name = Name, ParametersSchema = new JObject() } };

            public bool Contains(string name) => name == Name;
            public Tool Get(string name) => new Tool { Name = Name, ParametersSchema = new JObject() };
        }

        class FakeParser : IToolCallParser
        {
            public ToolCall Inline;
            public bool ThrowParams;

            public InlineToolCall ExtractInlineToolCall(string text)
            {
                return new InlineToolCall(Inline, text);
            }

            public object[] ParseToolParams(string tool, JObject args)
            {
                if (ThrowParams) throw new ToolValidationException("param", "bad");
                return new object[] { };
            }
        }

        LLMTextRequest Req() => new LLMTextRequest(new Conversation());
        LLMStreamChunk Txt(string t) => new LLMStreamChunk(StreamKind.Text, t);
        LLMStreamChunk Delta(string name, string d) =>
            new LLMStreamChunk(StreamKind.ToolCallDelta, new ToolCallDelta { Name = name, Delta = d });

        TextHandler Handler(FakeParser p = null, FakeCatalog c = null)
            => new TextHandler(p ?? new FakeParser(), c ?? new FakeCatalog(), NullLogger<TextHandler>.Instance);

        // ================================================================
        // ✔️ TEST 1: PURE TEXT
        // ================================================================
        [Fact]
        public void PureText_NoToolCall()
        {
            var h = Handler();
            h.PrepareRequest(Req());

            h.HandleChunk(Txt("hello "));
            h.HandleChunk(Txt("world"));

            var r = (LLMTextResponse)h.BuildResponse(FinishReason.Stop);

            Assert.Equal("hello world", r.AssistantMessage);
            Assert.Null(r.ToolCall);
        }

        // ================================================================
        // ✔️ TEST 2: REALISTIC INLINE TOOL CALL (SPLIT ACROSS CHUNKS)
        // This is how LLM actually streams inline JSON — never in one chunk.
        // ================================================================
        [Fact]
        public void InlineToolCall_SplitAcrossChunks()
        {
            var inlineCall = new ToolCall("a1", "Add", JObject.Parse("{\"x\":1}"));
            var parser = new FakeParser { Inline = inlineCall };
            var catalog = new FakeCatalog();

            var h = Handler(parser, catalog);
            h.PrepareRequest(Req());

            h.HandleChunk(Txt("Okay running now {\"na"));
            h.HandleChunk(Txt("me\":\"Add\",\"argu"));
            h.HandleChunk(Txt("ments\":{\"x\":1}} done"));

            var r = (LLMTextResponse)h.BuildResponse(FinishReason.Stop);

            Assert.Equal("Add", r.ToolCall.Name);
        }

        // ================================================================
        // ✔️ TEST 3: TOOLCALL DELTA → FINAL TOOLCALL (REAL OPENAI BEHAVIOR)
        // ================================================================
        [Fact]
        public void StreamingToolCall_DeltaAndFinal()
        {
            var parser = new FakeParser();
            var catalog = new FakeCatalog();

            var h = Handler(parser, catalog);
            h.PrepareRequest(Req());

            h.HandleChunk(Txt("Working..."));
            h.HandleChunk(Delta("Add", "{\"x\":"));
            h.HandleChunk(Delta("Add", "1}"));

            var finalCall = new ToolCall("id", "Add", JObject.Parse("{\"x\":1}"));
            h.HandleChunk(new LLMStreamChunk(StreamKind.ToolCallDelta, finalCall));

            var r = (LLMTextResponse)h.BuildResponse(FinishReason.Stop);

            Assert.Equal("Add", r.ToolCall.Name);
        }

        // ================================================================
        // ✔️ TEST 4: MALFORMED INLINE JSON SHOULD NOT TRIGGER
        // ================================================================
        [Fact]
        public void BadInlineJson_DoesNotTrigger()
        {
            var parser = new FakeParser { Inline = null }; // parser sees no valid inline call
            var h = Handler(parser);
            h.PrepareRequest(Req());

            h.HandleChunk(Txt("hi {\"name\":\"Add\""));
            h.HandleChunk(Txt(", \"arguments\": BROKEN }"));

            var r = (LLMTextResponse)h.BuildResponse(FinishReason.Stop);

            Assert.Null(r.ToolCall);
        }

        // ================================================================
        // ✔️ TEST 5: UNKNOWN TOOL → THROW
        // ================================================================
        [Fact]
        public void UnknownTool_Throws()
        {
            var parser = new FakeParser
            {
                Inline = new ToolCall("x1", "NotARealTool", new JObject())
            };

            var catalog = new FakeCatalog { Name = "Add" };

            var h = Handler(parser, catalog);
            h.PrepareRequest(Req());

            h.HandleChunk(Txt("hey {\"something\":1}"));

            Assert.Throws<RetryException>(() => h.BuildResponse(FinishReason.Stop));
        }

        // ================================================================
        // ✔️ TEST 6: BAD PARAMS → THROW
        // ================================================================
        [Fact]
        public void BadParams_Throws()
        {
            var parser = new FakeParser
            {
                Inline = new ToolCall("a9", "Add", JObject.Parse("{\"x\":999}")),
                ThrowParams = true
            };

            var catalog = new FakeCatalog();

            var h = Handler(parser, catalog);
            h.PrepareRequest(Req());

            h.HandleChunk(Txt("running... {\"foo\":1}"));

            Assert.Throws<RetryException>(() => h.BuildResponse(FinishReason.Stop));
        }
    }
}
