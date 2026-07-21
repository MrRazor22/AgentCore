using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Memory;
using AgentCore.Tools;
using AgentCore.LLM.Tornado;
using Microsoft.Extensions.Logging;

namespace AgentCore.Example;

/// <summary>
/// Manages the state, persistence, and execution of a single ChatGPT-like chat session.
/// </summary>
public class ChatSession
{
    private readonly string _apiKey;
    private readonly string _modelName;
    private readonly Uri? _baseUrl;
    private readonly ILoggerFactory _loggerFactory;
    private IMemory? _memory;

    private readonly Action<LLMEvent> _onLlmEvent;

    public IAgent Agent { get; private set; } = null!;
    public string SessionFile { get; private set; }
    public IReadOnlyList<Message> Messages => _memory?.Messages ?? Array.Empty<Message>();

    public ChatSession(string apiKey, string modelName, Uri? baseUrl, ILoggerFactory loggerFactory, string sessionFile, Action<LLMEvent> onLlmEvent)
    {
        _apiKey = apiKey;
        _modelName = modelName;
        _baseUrl = baseUrl;
        _loggerFactory = loggerFactory;
        SessionFile = sessionFile;
        _onLlmEvent = onLlmEvent ?? throw new ArgumentNullException(nameof(onLlmEvent));
    }

    public static async Task<ChatSession> CreateAsync(
        string apiKey, 
        string modelName, 
        Uri? baseUrl, 
        ILoggerFactory loggerFactory, 
        string sessionFile, 
        Action<LLMEvent> onLlmEvent,
        CancellationToken ct = default)
    {
        var session = new ChatSession(apiKey, modelName, baseUrl, loggerFactory, sessionFile, onLlmEvent);
        await session.InitializeAsync(ct);
        return session;
    }

    /// <summary>
    /// Builds the Agent using AgentCore builder API and initializes session messages if available.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    { 
        var provider = TornadoLLMFactory.CreateLLMProvider(_apiKey, _modelName, null, _baseUrl);
        var profileFile = "active_session_profile.json";

        var builder = AgentCore.Agent.Create()
            .WithLLM(provider)
            .WithTools(new WorkspaceTools())
            .AddLlmLayer(new StreamingLLMLayer(_onLlmEvent)) 
            .AddToolingLayer(new UserApprovalToolLayer()) 
            .AddMemoryLayer(new UserMemoryLayer(provider, profileFile, _loggerFactory))
            .AddMemoryLayer(new FileMemory(SessionFile))
            .WithLoggerFactory(_loggerFactory);

        Agent = builder.Build();
        _memory = builder.GetRequiredService<IMemory>();
        
        if (File.Exists(SessionFile))
        {
            var json = await File.ReadAllTextAsync(SessionFile, ct).ConfigureAwait(false);
            var messages = JsonSerializer.Deserialize<List<Message>>(json) ?? new();
            await _memory.RestoreAsync(messages, ct).ConfigureAwait(false);
        }
    }

    public async Task StartNewAsync(CancellationToken ct = default)
    {
        if (_memory != null)
        {
            await _memory.ClearAsync(ct);
        }
        
        var profileFile = "active_session_profile.json";
        if (File.Exists(profileFile))
        {
            File.Delete(profileFile);
        }
    }

    public async Task RevertToAsync(int index, CancellationToken ct = default)
    {
        var remainingMessages = Messages.Take(index).ToList();
        if (_memory != null)
        {
            await _memory.RestoreAsync(remainingMessages, ct);
        }
    }

    public async Task LoadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Session file not found", path);
        }
        File.Copy(path, SessionFile, overwrite: true);
        var json = await File.ReadAllTextAsync(SessionFile, ct).ConfigureAwait(false);
        var messages = JsonSerializer.Deserialize<List<Message>>(json) ?? new();
        if (_memory != null)
        {
            await _memory.RestoreAsync(messages, ct).ConfigureAwait(false);
        }
    }

    public void Save(string path)
    {
        File.Copy(SessionFile, path, overwrite: true);
    }
}
