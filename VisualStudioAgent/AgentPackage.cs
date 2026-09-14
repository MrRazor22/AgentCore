using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace VisualStudioAgent
{
    /// <summary>
    /// VSSDK In-Process Package providing registration for AgentChatToolWindowPane.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideToolWindow(typeof(AgentChatToolWindowPane), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")] // ToolWindowGuid.SolutionExplorer
    public sealed class AgentPackage : AsyncPackage
    {
        public const string PackageGuidString = "69cf361b-ecf6-4993-9c88-517865c378e1";

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        }
    }
}
