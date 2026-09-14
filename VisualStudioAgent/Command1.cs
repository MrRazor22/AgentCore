using Microsoft;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace VisualStudioAgent
{
    /// <summary>
    /// Command handler that opens the Devin Agent Chat ToolWindow.
    /// </summary>
    [VisualStudioContribution]
    internal class Command1 : Command
    {
        private readonly TraceSource logger;

        public Command1(TraceSource traceSource)
        {
            this.logger = Requires.NotNull(traceSource, nameof(traceSource));
        }

        public override CommandConfiguration CommandConfiguration => new("%VisualStudioAgent.Command1.DisplayName%")
        {
            Icon = new(ImageMoniker.KnownValues.Extension, IconSettings.IconAndText),
            Placements = [CommandPlacement.KnownPlacements.ExtensionsMenu],
        };

        private static IVsWindowFrame? s_windowFrame;
        private static AgentChatToolWindowPane? s_pane;
        private static System.Windows.Window? s_floatingWindow;

        public override Task InitializeAsync(CancellationToken cancellationToken)
        {
            return base.InitializeAsync(cancellationToken);
        }

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                // 1. If we already created a tool window frame, show it
                if (s_windowFrame != null)
                {
                    int hr = s_windowFrame.Show();
                    if (hr >= 0) return;
                }

                // 2. Try creating a native VS Tool Window dynamically via IVsUIShell
                var uiShell = (IVsUIShell?)await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(SVsUIShell));
                if (uiShell != null)
                {
                    s_pane = new AgentChatToolWindowPane();
                    Guid emptyGuid = Guid.Empty;
                    Guid persistenceGuid = typeof(AgentChatToolWindowPane).GUID;
                    int[] pos = new int[1];

                    int hr = uiShell.CreateToolWindow(
                        (uint)__VSCREATETOOLWIN.CTW_fInitNew,
                        0,
                        s_pane,
                        ref emptyGuid,
                        ref persistenceGuid,
                        ref emptyGuid,
                        null,
                        "Devin Agent Chat",
                        pos,
                        out s_windowFrame);

                    if (hr >= 0 && s_windowFrame != null)
                    {
                        s_windowFrame.Show();
                        return;
                    }
                }

                // 3. Fallback: Show as native in-process WPF Window
                ShowWpfWindowFallback();
            }
            catch (Exception ex)
            {
                try
                {
                    ShowWpfWindowFallback();
                }
                catch (Exception fallbackEx)
                {
                    System.Windows.MessageBox.Show(
                        $"Failed to open Devin Chat: {ex.Message}\nFallback: {fallbackEx.Message}",
                        "Devin Agent Chat Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
        }

        private static void ShowWpfWindowFallback()
        {
            if (s_floatingWindow == null || !s_floatingWindow.IsLoaded)
            {
                s_floatingWindow = new System.Windows.Window
                {
                    Title = "Devin Agent Chat",
                    Width = 480,
                    Height = 740,
                    Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#0d1117")!,
                    Content = new AgentChatControl(),
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen
                };
                s_floatingWindow.Closed += (s, e) => s_floatingWindow = null;
            }

            s_floatingWindow.Show();
            s_floatingWindow.Activate();
        }
    }
}
