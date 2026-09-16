using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using VisualStudioAgent.Abstractions;
using VisualStudioAgent.Services;

namespace VisualStudioAgent;

public partial class AgentChatControl : UserControl
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly IHostProcessManager _hostManager;
    private readonly IVsAdapter _vsAdapter;
    private readonly ITodoManager _todoManager;

    public AgentChatControl()
    {
        InitializeComponent();
        _todoManager = new TodoManager();
        _vsAdapter = new VsAdapter(new VsEditorService(), new VsSearchService(), new VsTerminalService(), _todoManager);
        _hostManager = new HostProcessManager();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        _ = InitializeAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _hostManager.Stop();

    private async Task InitializeAsync()
    {
        try
        {
            string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisualStudioAgent", "WebView2");
            Directory.CreateDirectory(dataFolder);

            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, dataFolder);
            await webView.EnsureCoreWebView2Async(env);
            webView.CoreWebView2.WebMessageReceived += (_, e) => _ = _hostManager.SendAsync(e.TryGetWebMessageAsString());

            _todoManager.TodosChanged += todos =>
            {
                _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var json = JsonSerializer.Serialize(new { type = "plan", todos }, JsonOpts);
                    webView.CoreWebView2?.PostWebMessageAsString(json);
                });
            };

            await _hostManager.StartAsync(HandleHostMessageAsync);

            string html = Path.Combine(Path.GetDirectoryName(typeof(AgentChatControl).Assembly.Location) ?? "", "wwwroot", "chat.html");
            if (File.Exists(html)) webView.CoreWebView2.Navigate(new Uri(html).AbsoluteUri);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Initialization failed: {ex.Message}", "Devin Agent", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task HandleHostMessageAsync(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "tool_call")
            {
                string id = root.GetProperty("id").GetString() ?? "";
                string name = root.GetProperty("name").GetString() ?? "";
                var args = root.TryGetProperty("args", out var a) ? a : default;

                var (result, isError) = await _vsAdapter.DispatchAsync(name, args);
                var responseJson = JsonSerializer.Serialize(new { type = "tool_result", id, result, error = isError }, JsonOpts);
                await _hostManager.SendAsync(responseJson);
                return;
            }
        }
        catch { }

        _ = webView.Dispatcher.InvokeAsync(() =>
        {
            try { webView.CoreWebView2?.PostWebMessageAsString(line); } catch { }
        });
    }
}
