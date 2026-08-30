using AgentCore.LLM.Tornado;
using LlmTornado;
using LlmTornado.Chat.Models;
using LlmTornado.Code;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace AgentCore.Tests.Integration;

public static class OpenAICompatibleFixture
{
    private static readonly bool _enabled;
    private static readonly string _baseUrl;
    private static readonly string _apiKey;
    private static readonly string _modelName;
    private static readonly int _contextWindow;
    private static readonly int _reservedTokens;

    static OpenAICompatibleFixture()
    {
        // 1. Establish Defaults
        _enabled = false;
        _baseUrl = "http://127.0.0.1:1234/v1";
        _apiKey = "lm-studio";
        _modelName = "model";
        _contextWindow = 128000;
        _reservedTokens = 2000;

        // 2. Load from appsettings.json if it exists
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("LiveTests", out var section))
                {
                    if (section.TryGetProperty("Enabled", out var enabledProp))
                        _enabled = enabledProp.GetBoolean();
                    if (section.TryGetProperty("Url", out var urlProp))
                        _baseUrl = urlProp.GetString() ?? _baseUrl;
                    if (section.TryGetProperty("ApiKey", out var keyProp))
                        _apiKey = keyProp.GetString() ?? _apiKey;
                    if (section.TryGetProperty("Model", out var modelProp))
                        _modelName = modelProp.GetString() ?? _modelName;
                    if (section.TryGetProperty("ContextWindow", out var contextWindowProp))
                        _contextWindow = contextWindowProp.GetInt32();
                    if (section.TryGetProperty("ReservedTokens", out var reservedTokensProp))
                        _reservedTokens = reservedTokensProp.GetInt32();
                }
            }
        }
        catch { }

        // 3. Environment variable override for API key (secret)
        var envKey = Environment.GetEnvironmentVariable("AGENTCORE_LIVE_KEY");
        if (!string.IsNullOrEmpty(envKey))
        {
            _apiKey = envKey;
        }
    }

    public static bool ShouldRunLiveTests() => _enabled;
    public static string GetBaseUrl() => _baseUrl;
    public static string GetApiKey() => _apiKey;
    public static string GetModelName() => _modelName;
    public static int GetContextWindow() => _contextWindow;

    public static bool IsEndpointReachable()
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(2);
            var url = GetBaseUrl();
            var response = client.GetAsync($"{url.TrimEnd('/')}/models").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static (TornadoApi Api, ChatModel Model) CreateTornado()
    {
        var baseUrl = GetBaseUrl();
        var apiKey = GetApiKey();
        var modelName = GetModelName();

        var api = new TornadoApi(new List<ProviderAuthentication>
        {
            new ProviderAuthentication(LLmProviders.Custom, apiKey, baseUrl)
        });
        var model = new ChatModel(modelName, LLmProviders.Custom);
        return (api, model);
    }
}

public class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (!OpenAICompatibleFixture.ShouldRunLiveTests())
        {
            Skip = "Skipping live LLM integration tests. Set 'LiveTests:Enabled' to true in appsettings.json to enable.";
        }
        else if (!OpenAICompatibleFixture.IsEndpointReachable())
        {
            Skip = $"Live integration tests enabled, but endpoint {OpenAICompatibleFixture.GetBaseUrl()} is not reachable.";
        }
    }
}
