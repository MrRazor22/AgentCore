using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Providers.Tornado;
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

    public IAgent Agent { get; private set; } = null!;
    public PersistentMemoryLayer Memory { get; private set; } = null!;
    public string SessionFile { get; private set; }

    public ChatSession(string apiKey, string modelName, Uri? baseUrl, ILoggerFactory loggerFactory, string sessionFile)
    {
        _apiKey = apiKey;
        _modelName = modelName;
        _baseUrl = baseUrl;
        _loggerFactory = loggerFactory;
        SessionFile = sessionFile;

        Rebuild(new List<Message>());
    }

    /// <summary>
    /// Builds the Agent using AgentCore builder API and seeds it with messages.
    /// </summary>
    public void Rebuild(List<Message> seedMessages)
    {
        Memory = new PersistentMemoryLayer(SessionFile);
        var tokenCounter = new ApproximateTokenCounter();

        Agent = AgentCore.Agent.Create()
            .AddTornado(_apiKey, new[] { new LLMMetadata(_modelName, 128000) }, _baseUrl)
            .WithTools<ExampleTools>()
            .WithTokenCounter(tokenCounter)
            .AddMemoryLayer(Memory.Initialize)
            .AddToolingLayer(inner => new UserApprovalToolingLayer(inner))
            .AddLLMLayer(inner => new PerformanceLoggingLlmLayer(inner, tokenCounter, 128000))
            .WithLoggerFactory(_loggerFactory)
            .Build();

        if (seedMessages.Count > 0)
        {
            Memory.SetLocalMessages(seedMessages);
            Memory.RememberAsync(seedMessages).GetAwaiter().GetResult();
        }
    }

    public void StartNew()
    {
        if (File.Exists(SessionFile))
        {
            File.Delete(SessionFile);
        }
        Rebuild(new List<Message>());
    }

    public void RevertTo(int index)
    {
        var messages = Memory.GetLocalMessages().Take(index).ToList();
        Rebuild(messages);
    }

    public void Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Session file not found", path);
        }
        var json = File.ReadAllText(path);
        var messages = JsonSerializer.Deserialize<List<Message>>(json) ?? new List<Message>();
        SessionFile = path;
        Rebuild(messages);
    }

    public void Save(string path)
    {
        File.Copy(SessionFile, path, overwrite: true);
    }
}
