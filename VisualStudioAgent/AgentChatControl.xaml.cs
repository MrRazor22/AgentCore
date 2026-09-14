using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace VisualStudioAgent
{
    /// <summary>
    /// Interaction logic for AgentChatControl.xaml
    /// Bridges WebView2 UI with AgentCore.Host via Windows Named Pipes.
    /// </summary>
    public partial class AgentChatControl : UserControl
    {
        private static Process? s_agentHostProcess;
        private static NamedPipeClientStream? s_pipeClient;
        private static StreamWriter? s_pipeWriter;
        private static StreamReader? s_pipeReader;
        private static readonly SemaphoreSlim s_writeLock = new(1, 1);
        private static readonly string s_pipeName = $"AgentCore_VS_{Process.GetCurrentProcess().Id}";

        public AgentChatControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            _ = InitializeAllAsync();
        }

        private async Task InitializeAllAsync()
        {
            await InitializeWebViewAsync();
            await EnsureHostAndConnectPipeAsync();
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VisualStudioAgent",
                    "WebView2");
                Directory.CreateDirectory(userDataFolder);

                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await webView.EnsureCoreWebView2Async(env);

                webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                string assemblyDir = Path.GetDirectoryName(typeof(AgentChatControl).Assembly.Location) ?? string.Empty;
                string htmlPath = Path.Combine(assemblyDir, "wwwroot", "chat.html");

                if (File.Exists(htmlPath))
                {
                    webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
                }
                else
                {
                    string searchDir = assemblyDir;
                    for (int i = 0; i < 5 && !string.IsNullOrEmpty(searchDir); i++)
                    {
                        string candidate = Path.Combine(searchDir, "wwwroot", "chat.html");
                        if (File.Exists(candidate))
                        {
                            webView.CoreWebView2.Navigate(new Uri(candidate).AbsoluteUri);
                            return;
                        }
                        var parent = Directory.GetParent(searchDir);
                        searchDir = parent?.FullName ?? string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize WebView2: {ex.Message}");
                MessageBox.Show($"WebView2 initialization failed: {ex.Message}", "Devin Agent Chat Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnWebMessageReceived(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            string message = e.TryGetWebMessageAsString();
            _ = SendMessageToPipeAsync(message);
        }

        private static async Task SendMessageToPipeAsync(string message)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(message) && s_pipeWriter != null)
                {
                    await s_writeLock.WaitAsync();
                    try
                    {
                        await s_pipeWriter.WriteLineAsync(message);
                    }
                    finally
                    {
                        s_writeLock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending message to pipe: {ex.Message}");
            }
        }

        private async Task EnsureHostAndConnectPipeAsync()
        {
            try
            {
                // 1. Launch AgentCore.Host.exe if not running
                if (s_agentHostProcess == null || s_agentHostProcess.HasExited)
                {
                    string? exe = FindHostExecutable();
                    if (exe != null && File.Exists(exe))
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = exe,
                            Arguments = $"--pipe {s_pipeName}",
                            WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        s_agentHostProcess = Process.Start(psi);
                    }
                }

                // 2. Connect to Named Pipe
                s_pipeClient = new NamedPipeClientStream(".", s_pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await s_pipeClient.ConnectAsync(6000);

                s_pipeWriter = new StreamWriter(s_pipeClient, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
                s_pipeReader = new StreamReader(s_pipeClient, Encoding.UTF8, false, 1024, leaveOpen: true);

                // 3. Start background reader loop to forward events to WebView2
                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (s_pipeClient != null && s_pipeClient.IsConnected)
                        {
                            string? line = await s_pipeReader.ReadLineAsync();
                            if (line == null) break;

                            await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            try
                            {
                                webView.CoreWebView2?.PostWebMessageAsString(line);
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Pipe read loop error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to connect to Named Pipe: {ex.Message}");
            }
        }

        private static string? FindHostExecutable()
        {
            string? envPath = Environment.GetEnvironmentVariable("AGENTCORE_HOST_PATH");
            if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath)) return envPath;

            try
            {
                Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
                var dte = (EnvDTE.DTE?)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte?.Solution != null && !string.IsNullOrEmpty(dte.Solution.FullName))
                {
                    string? slnDir = Path.GetDirectoryName(dte.Solution.FullName);
                    if (!string.IsNullOrEmpty(slnDir))
                    {
                        string candidate = Path.Combine(slnDir, "AgentCore.Host", "bin", "Debug", "net10.0", "AgentCore.Host.exe");
                        if (File.Exists(candidate)) return candidate;
                        candidate = Path.Combine(slnDir, "AgentCore.Host", "bin", "Release", "net10.0", "AgentCore.Host.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                }
            }
            catch { }

            string devPath = @"D:\CodeBase\AgentCore-Main\AgentCore.Host\bin\Debug\net10.0\AgentCore.Host.exe";
            if (File.Exists(devPath)) return devPath;

            string assemblyDir = Path.GetDirectoryName(typeof(AgentChatControl).Assembly.Location) ?? string.Empty;
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                string candidate = Path.Combine(assemblyDir, "AgentCore.Host.exe");
                if (File.Exists(candidate)) return candidate;
                candidate = Path.Combine(assemblyDir, "AgentCore.Host", "AgentCore.Host.exe");
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }
    }
}
