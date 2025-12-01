using AgentCore.Chat;
using AgentCore.Runtime;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentCore.Tests.Runtime
{
    public sealed class FileMemory_Tests : IDisposable
    {
        private readonly string _tempDir;

        public FileMemory_Tests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        private FileMemory NewMemory()
        {
            return new FileMemory(new FileMemoryOptions
            {
                PersistDir = _tempDir
            });
        }

        [Fact]
        public async Task RecallAsync_NoFile_ReturnsEmptyConversation_AndCaches()
        {
            var mem = NewMemory();
            var convo1 = await mem.RecallAsync("s1", "hi");
            Assert.Empty(convo1);

            var convo2 = await mem.RecallAsync("s1", "hi again");
            Assert.Same(convo1, convo2); // cached instance
        }

        [Fact]
        public async Task UpdateAsync_CreatesFile_AndAddsMessages()
        {
            var mem = NewMemory();
            await mem.UpdateAsync("s1", "hello", "world");

            var path = Path.Combine(_tempDir, "s1.json");
            Assert.True(File.Exists(path));

            var json = File.ReadAllText(path);
            var arr = JArray.Parse(json);

            Assert.Equal("user", arr[0]["role"]?.ToString());
            Assert.Equal("hello", arr[0]["content"]?.ToString());

            Assert.Equal("assistant", arr[1]["role"]?.ToString());
            Assert.Equal("world", arr[1]["content"]?.ToString());
        }

        [Fact]
        public async Task RecallAsync_LoadsExistingFile_AndCaches()
        {
            var mem = NewMemory();

            // manually create a stored conversation
            var path = Path.Combine(_tempDir, "s1.json");
            File.WriteAllText(path,
                """
            [
              { "role": "user", "content": "old" }
            ]
            """
            );

            var convo1 = await mem.RecallAsync("s1", "x");
            Assert.Single(convo1);
            Assert.Equal("user", convo1[0].Role.ToString().ToLower());
            Assert.Equal("old", ((TextContent)convo1[0].Content).Text);

            // second recall should return SAME cached instance
            var convo2 = await mem.RecallAsync("s1", "y");
            Assert.Same(convo1, convo2);
        }

        [Fact]
        public async Task UpdateAsync_UsesCachedConversation_AndAppendsMessages()
        {
            var mem = NewMemory();

            // seed in file
            var path = Path.Combine(_tempDir, "s1.json");
            File.WriteAllText(path,
                """
            [
              { "role": "user", "content": "old" }
            ]
            """
            );

            // first load -> cached
            var initial = await mem.RecallAsync("s1", "req");

            await mem.UpdateAsync("s1", "newQ", "newA");

            // must append to same cached instance
            Assert.Equal(3, initial.Count);

            Assert.Equal("old", ((TextContent)initial[0].Content).Text);
            Assert.Equal("newQ", ((TextContent)initial[1].Content).Text);
            Assert.Equal("newA", ((TextContent)initial[2].Content).Text);
        }

        [Fact]
        public async Task UpdateAsync_WhenDifferentSessionId_UsesNewCache()
        {
            var mem = NewMemory();

            await mem.UpdateAsync("s1", "q1", "a1");
            var s1 = await mem.RecallAsync("s1", "x");

            await mem.UpdateAsync("s2", "q2", "a2");
            var s2 = await mem.RecallAsync("s2", "x");

            Assert.NotSame(s1, s2);
            Assert.Single(s2.Where(c => ((TextContent)c.Content).Text == "q2"));
        }
    }
}
