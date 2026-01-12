using AgentCore.Chat;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AgentCore.Runtime
{
    public interface IAgentMemory
    {
        Task<Conversation> RecallAsync(string sessionId, string userRequest);
        Task UpdateAsync(string sessionId, string userRequest, string response);
        Task ClearAsync(string sessionId);
    }

    public sealed class FileMemoryOptions
    {
        /// <summary>
        /// Set null to completely disable memory
        /// </summary>
        public string PersistDir { get; set; }

        /// <summary>
        /// 0 = unlimited, N = last N messages
        /// </summary>
        public int MaxChatHistory { get; set; }

        public FileMemoryOptions()
        {
            PersistDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AgentCore");
            MaxChatHistory = 0;
        }
    }

    public sealed class FileMemory : IAgentMemory
    {
        private readonly FileMemoryOptions _options;
        private string _cachedSessionId;
        private Conversation _cached;

        public FileMemory(IOptions<FileMemoryOptions> options)
        {
            _options = options?.Value ?? new FileMemoryOptions();

            if (_options.PersistDir != null)
                Directory.CreateDirectory(_options.PersistDir);
        }

        public Task<Conversation> RecallAsync(string sessionId, string userRequest)
        {
            if (_options.PersistDir == null)
                return Task.FromResult(new Conversation());

            if (_cachedSessionId == sessionId && _cached != null)
                return Task.FromResult(_cached);

            _cachedSessionId = sessionId;
            _cached = new Conversation();

            var file = Path.Combine(_options.PersistDir, sessionId + ".json");
            if (!File.Exists(file))
                return Task.FromResult(_cached);

            var json = File.ReadAllText(file);
            _cached = JsonConvert.DeserializeObject<Conversation>(json) ?? new Conversation();
            return Task.FromResult(_cached);
        }

        public Task UpdateAsync(string sessionId, string userRequest, string response)
        {
            if (_options.PersistDir == null)
                return Task.FromResult(0);

            if (_cachedSessionId != sessionId || _cached == null)
                _cached = RecallAsync(sessionId, userRequest).Result;

            _cached.AddUser(userRequest);
            _cached.AddAssistant(response);

            TrimHistory(_cached);

            var file = Path.Combine(_options.PersistDir, sessionId + ".json");
            File.WriteAllText(file, _cached.ToJson());

            return Task.FromResult(0);
        }

        public Task ClearAsync(string sessionId)
        {
            if (_cachedSessionId == sessionId)
            {
                _cached = null;
                _cachedSessionId = null;
            }

            if (_options.PersistDir == null)
                return Task.FromResult(0);

            var file = Path.Combine(_options.PersistDir, sessionId + ".json");
            if (File.Exists(file))
                File.Delete(file);

            return Task.FromResult(0);
        }

        private void TrimHistory(Conversation convo)
        {
            if (_options.MaxChatHistory <= 0)
                return;

            while (convo.Count > _options.MaxChatHistory)
                convo.RemoveAt(0);
        }
    }
}
