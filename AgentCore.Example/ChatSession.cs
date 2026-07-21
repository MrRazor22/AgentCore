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
    private PersistentMemoryLayer? _persistentMemory;

    public IAgent Agent { get; private set; } = null!;
    public string SessionFile { get; private set; }
    public IReadOnlyList<Message> Messages => _persistentMemory?.GetLocalMessages() ?? Array.Empty<Message>();

    public ChatSession(string apiKey, string modelName, Uri? baseUrl, ILoggerFactory loggerFactory, string sessionFile)
    {
        _apiKey = apiKey;
        _modelName = modelName;
        _baseUrl = baseUrl;
        _loggerFactory = loggerFactory;
        SessionFile = sessionFile;
    }

    public static async Task<ChatSession> CreateAsync(
        string apiKey, 
        string modelName, 
        Uri? baseUrl, 
        ILoggerFactory loggerFactory, 
        string sessionFile, 
        CancellationToken ct = default)
    {
        var session = new ChatSession(apiKey, modelName, baseUrl, loggerFactory, sessionFile);
        await session.InitializeAsync(ct);
        return session;
    }

    /// <summary>
    /// Builds the Agent using AgentCore builder API and initializes session messages if available.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var tokenCounter = new ApproximateTokenCounter();
        PersistentMemoryLayer? persistentMemory = null;

        Agent = AgentCore.Agent.Create()
            .WithLLM(TornadoLLMFactory.CreateLLMProvider(_apiKey, _modelName, null, _baseUrl))
            .AddLlmLayer(inner => new RetryingLLM(inner))
            .AddLlmLayer(inner => new PerformanceLoggingLlmLayer(inner, tokenCounter))
            .WithTools(new ExampleTools())
            .AddToolingLayer(inner => new UserApprovalToolingLayer(inner))
            .WithTokenCounter(tokenCounter)
            .AddMemoryLayer(inner => persistentMemory = new PersistentMemoryLayer(inner, SessionFile))
            .WithLoggerFactory(_loggerFactory)
            .Build();

        if (persistentMemory == null)
        {
            throw new InvalidOperationException("PersistentMemoryLayer was not created during Build().");
        }

        _persistentMemory = persistentMemory;
        await _persistentMemory.LoadAsync(ct);
    }

    public async Task StartNewAsync(CancellationToken ct = default)
    {
        if (_persistentMemory != null)
        {
            await _persistentMemory.ClearAsync(ct);
        }
    }

    public async Task RevertToAsync(int index, CancellationToken ct = default)
    {
        var remainingMessages = Messages.Take(index).ToList();
        if (_persistentMemory != null)
        {
            await _persistentMemory.RestoreAsync(remainingMessages, ct);
        }
    }

    public async Task LoadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Session file not found", path);
        }
        if (_persistentMemory != null)
        {
            await _persistentMemory.ReloadFromDiskAsync(path, ct);
        }
    }

    public void Save(string path)
    {
        File.Copy(SessionFile, path, overwrite: true);
    }
}
