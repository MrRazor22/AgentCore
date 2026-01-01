using AgentCore.Chat;
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
    public sealed class AgentMemoryOptions
    {
        /// <summary>
        /// Set null to disable memory presistance
        /// </summary>
        public string? PersistDir { get; set; }
            = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AgentCore");

        /// <summary>
        /// 0 = unlimited, 1 = last response only, N = last N messages
        /// </summary>
        public int MaxChatHistory { get; set; } = 0;
    }

    public sealed class FileMemory : IAgentMemory
    {
        private readonly AgentMemoryOptions _options;
        private string? _cachedSessionId;
        private Conversation? _cached;
        public FileMemory(AgentMemoryOptions? options = null)
        {
            _options = options ?? new AgentMemoryOptions();

            if (_options.PersistDir != null)
                Directory.CreateDirectory(_options.PersistDir);
        }

        public async Task<Conversation> RecallAsync(string sessionId, string userRequest)
        {
            if (_cachedSessionId == sessionId && _cached != null)
                return _cached;

            _cachedSessionId = sessionId;
            _cached = new Conversation();

            if (_options.PersistDir == null)
                return _cached;

            var file = Path.Combine(_options.PersistDir, $"{sessionId}.json");
            if (!File.Exists(file))
                return _cached;

            var json = await Task.Run(() => File.ReadAllText(file))
                     .ConfigureAwait(false);
            _cached = JsonConvert.DeserializeObject<Conversation>(json) ?? new Conversation();
            return _cached;
        }
        public async Task UpdateAsync(string sessionId, string userRequest, string response)
        {
            if (_cachedSessionId != sessionId || _cached == null)
                _cached = await RecallAsync(sessionId, userRequest).ConfigureAwait(false);

            _cached.AddUser(userRequest);
            _cached.AddAssistant(response);

            TrimHistory(_cached);

            if (_options.PersistDir == null)
                return;

            var file = Path.Combine(_options.PersistDir, $"{sessionId}.json");
            var json = _cached.ToJson();
            await Task.Run(() => File.WriteAllText(file, json))
                .ConfigureAwait(false);
        }
        public Task ClearAsync(string sessionId)
        {
            if (_cachedSessionId == sessionId)
            {
                _cached = new Conversation();
                _cachedSessionId = sessionId;
            }

            if (_options.PersistDir == null)
                return Task.CompletedTask;

            var file = Path.Combine(_options.PersistDir, $"{sessionId}.json");
            if (File.Exists(file))
                File.Delete(file);

            return Task.CompletedTask;
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