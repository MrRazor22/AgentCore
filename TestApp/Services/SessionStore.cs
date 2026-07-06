using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace TestApp.Services;

public record SessionInfo(string Id, string Title, DateTime LastUpdated);

public class SessionStore
{
    private readonly string _sessionsDir;

    public SessionStore(string sessionsDir = "sessions")
    {
        _sessionsDir = Path.GetFullPath(sessionsDir);
        if (!Directory.Exists(_sessionsDir))
        {
            Directory.CreateDirectory(_sessionsDir);
        }
    }

    public async Task<IReadOnlyList<SessionInfo>> GetSessionsAsync()
    {
        var files = Directory.GetFiles(_sessionsDir, "*.json");
        var sessions = new List<SessionInfo>();

        foreach (var file in files)
        {
            var id = Path.GetFileNameWithoutExtension(file);
            var lastUpdated = File.GetLastWriteTimeUtc(file);
            
            string title = "New Chat Session";
            try
            {
                var content = await File.ReadAllTextAsync(file);
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var userMsg = doc.RootElement.EnumerateArray()
                        .FirstOrDefault(m => m.GetProperty("role").GetString() == "user");
                    if (userMsg.ValueKind != JsonValueKind.Undefined)
                    {
                        var contents = userMsg.GetProperty("contents");
                        if (contents.ValueKind == JsonValueKind.Array && contents.GetArrayLength() > 0)
                        {
                            var firstContent = contents[0];
                            if (firstContent.TryGetProperty("value", out var val))
                            {
                                var text = val.GetString() ?? "";
                                title = text.Length > 40 ? text[..37] + "..." : text;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback on read or parse failure
            }

            sessions.Add(new SessionInfo(id, title, lastUpdated));
        }

        return sessions.OrderByDescending(s => s.LastUpdated).ToList();
    }

    public void DeleteSession(string sessionId)
    {
        var path = Path.Combine(_sessionsDir, $"{sessionId}.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
