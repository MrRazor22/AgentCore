using AgentCore;
using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.MEAI;
using AgentCore.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

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
    private List<Message> _messages = new();
    private IContext? _memory;
    private readonly Action<ILLMOutput> _onLlmEvent;

    public IAgent Agent { get; private set; } = null!;
    public string SessionFile { get; private set; }
    public IReadOnlyList<Message> Messages => _messages;

    public ChatSession(string apiKey, string modelName, Uri? baseUrl, ILoggerFactory loggerFactory, string sessionFile, Action<ILLMOutput> onLlmEvent)
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
        Action<ILLMOutput> onLlmEvent,
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
        var clientOptions = new OpenAIClientOptions();
        if (_baseUrl != null)
        {
            var urlStr = _baseUrl.ToString();
            clientOptions.Endpoint = urlStr.Contains("/v1") 
                ? _baseUrl 
                : new Uri(urlStr.TrimEnd('/') + "/v1/");
        }
        var openAiClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential(_apiKey), clientOptions);
        var chatClient = openAiClient.GetChatClient(_modelName).AsIChatClient();

        var capabilities = new LLMCapabilities { ContextWindow = 15000, ReservedTokens = 5000 };
        var provider = new AgentCore.LLM.MEAI.MEAILLM(chatClient, capabilities);
        var profileFile = "active_session_profile.json";

        var builder = AgentCore.Agent.Create()
            .WithMEAI(chatClient, capabilities)
            .WithTools(new WorkspaceTools())
            .AddLLMLayer(new StreamingLLMLayer(_onLlmEvent)) 
            .AddToolingLayer(new UserApprovalToolLayer()) 
            .AddContextLayer(new UserMemoryLayer(provider, profileFile, _loggerFactory))
            .AddContextLayer(new FilePersistentChatContext(SessionFile))
            .WithLoggerFactory(_loggerFactory);

        Agent = builder.Build();
        _memory = builder.GetRequiredService<IContext>();
        
        await RefreshAsync(ct).ConfigureAwait(false);
        if (_messages.Count > 0)
        {
            await _memory.ClearAsync(ct).ConfigureAwait(false);
            await _memory.AddRangeAsync(_messages, ct).ConfigureAwait(false);
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (File.Exists(SessionFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(SessionFile, ct).ConfigureAwait(false);
                _messages = JsonSerializer.Deserialize<List<Message>>(json) ?? new();
            }
            catch
            {
                _messages = new();
            }
        }
        else
        {
            _messages = new();
        }
    }

    public async Task StartNewAsync(string sessionFile, CancellationToken ct = default)
    {
        SessionFile = sessionFile;
        await InitializeAsync(ct).ConfigureAwait(false);
        
        var profileFile = "active_session_profile.json";
        if (File.Exists(profileFile))
        {
            File.Delete(profileFile);
        }
    }

    public async Task RevertToAsync(int index, CancellationToken ct = default)
    {
        var remainingMessages = _messages.Take(index).ToList();
        _messages = remainingMessages;
        if (_memory != null)
        {
            await _memory.ClearAsync(ct);
            await _memory.AddRangeAsync(remainingMessages, ct);
        }
    }

    public async Task LoadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Session file not found", path);
        }
        SessionFile = path;
        await InitializeAsync(ct).ConfigureAwait(false);
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.Copy(SessionFile, path, overwrite: true);
    }

    public static async Task<string> GetSessionTitleAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath)) return "New Session";
        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var messages = JsonSerializer.Deserialize<List<Message>>(json);
            if (messages != null && messages.Count > 0)
            {
                var firstUserMsg = messages.FirstOrDefault(m => m.Role == Role.User) ?? messages.FirstOrDefault();
                if (firstUserMsg != null)
                {
                    var summary = string.Join(" ", firstUserMsg.Contents.Select(c => c.ForLlm()));
                    if (summary.Length > 50) summary = summary[..47] + "...";
                    return summary;
                }
            }
        }
        catch { }
        return "Empty Session";
    }
}
