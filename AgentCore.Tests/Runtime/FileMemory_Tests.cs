using AgentCore.Chat;
using AgentCore.Runtime;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace AgentCore.Tests.Runtime
{
    public sealed class FileMemory_Tests : IDisposable
    {
        private readonly string _dir;
        private readonly FileMemory _memory;

        public FileMemory_Tests()
        {
            _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);

            _memory = new FileMemory(new FileMemoryOptions
            {
                PersistDir = _dir
            });
        }

        [Fact]
        public async Task Recall_NoFile_Returns_EmptyConversation()
        {
            var convo = await _memory.RecallAsync("s1", "hello");

            Assert.NotNull(convo);
            Assert.Empty(convo);
        }

        [Fact]
        public async Task Update_Writes_Conversation_To_Disk()
        {
            await _memory.UpdateAsync("s1", "hi", "there");

            var file = Path.Combine(_dir, "s1.json");
            Assert.True(File.Exists(file));

            var json = File.ReadAllText(file);
            var convo = JsonConvert.DeserializeObject<Conversation>(json)!;

            Assert.Equal(2, convo.Count);
            Assert.Equal(Role.User, convo[0].Role);
            Assert.Equal(Role.Assistant, convo[1].Role);
        }

        [Fact]
        public async Task Recall_After_Update_Returns_Persisted_Conversation()
        {
            await _memory.UpdateAsync("s1", "q1", "a1");

            var convo = await _memory.RecallAsync("s1", "q2");

            Assert.Equal(2, convo.Count);
            Assert.Equal("q1", ((TextContent)convo[0].Content).Text);
            Assert.Equal("a1", ((TextContent)convo[1].Content).Text);
        }

        [Fact]
        public async Task Recall_Uses_Cache_For_Same_Session()
        {
            var c1 = await _memory.RecallAsync("s1", "x");
            var c2 = await _memory.RecallAsync("s1", "y");

            Assert.Same(c1, c2);
        }

        [Fact]
        public async Task Switching_Session_Resets_Cache()
        {
            await _memory.UpdateAsync("s1", "q1", "a1");

            var c1 = await _memory.RecallAsync("s1", "x");
            var c2 = await _memory.RecallAsync("s2", "y");

            Assert.NotSame(c1, c2);
            Assert.Empty(c2);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_dir, recursive: true);
            }
            catch
            {
                // ignore cleanup issues
            }
        }
    }
}
