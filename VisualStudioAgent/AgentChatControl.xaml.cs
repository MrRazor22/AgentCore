using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace VisualStudioAgent
{
    /// <summary>
    /// Interaction logic for AgentChatControl.xaml
    /// </summary>
    public partial class AgentChatControl : UserControl
    {
        public AgentChatControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            _ = InitializeWebViewAsync();
        }

        private async System.Threading.Tasks.Task InitializeWebViewAsync()
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

                string assemblyDir = Path.GetDirectoryName(typeof(AgentChatControl).Assembly.Location) ?? string.Empty;
                string htmlPath = Path.Combine(assemblyDir, "wwwroot", "chat.html");

                if (File.Exists(htmlPath))
                {
                    webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
                }
                else
                {
                    string devPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "chat.html");
                    if (File.Exists(devPath))
                    {
                        webView.CoreWebView2.Navigate(new Uri(devPath).AbsoluteUri);
                    }
                    else
                    {
                        // Fallback: search upwards for wwwroot/chat.html
                        string searchDir = assemblyDir;
                        for (int i = 0; i < 4 && !string.IsNullOrEmpty(searchDir); i++)
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize WebView2: {ex.Message}");
                MessageBox.Show($"WebView2 initialization failed: {ex.Message}", "Devin Agent Chat Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
