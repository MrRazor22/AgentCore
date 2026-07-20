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

    public IAgent Agent { get; private set; } = null!;
    public PersistentContextDecorator ContextDecorator { get; private set; } = null!;
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
        ContextDecorator = new PersistentContextDecorator(SessionFile);
        var tokenCounter = new ApproximateTokenCounter();

        var tornadoProvider = TornadoProvider.CreateLLMProvider(_apiKey, _modelName, new LLMCapabilities { ContextWindow = 128000 }, _baseUrl);
        var retryingProvider = new RetryingLLM(tornadoProvider);
        var loggedProvider = new PerformanceLoggingLlmLayer(retryingProvider, tokenCounter, 128000);

        var baseMemory = new RollingWindowMemory(
            tokenCounter,
            new LLMCapabilities { ContextWindow = 128000 },
            MethodTool.FromType(typeof(ExampleTools)).Cast<Tool>().ToList(),
            null);
        ContextDecorator.Initialize(baseMemory);

        Agent = AgentCore.Agent.Create()
            .WithProvider(loggedProvider)
            .WithTools(new ExampleTools())
            .WithTokenCounter(tokenCounter)
            .WithMemory(ContextDecorator)
            .AddToolingLayer(inner => new UserApprovalToolingLayer(inner))
            .WithLoggerFactory(_loggerFactory)
            .Build();

        if (seedMessages.Count > 0)
        {
            ContextDecorator.SetLocalMessages(seedMessages);
            ContextDecorator.RememberAsync(seedMessages).GetAwaiter().GetResult();
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
        var messages = ContextDecorator.GetLocalMessages().Take(index).ToList();
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
