using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;

namespace VisualStudioAgent
{
    /// <summary>
    /// VSSDK ToolWindowPane hosting the Devin-inspired Agent Chat WPF control.
    /// </summary>
    [Guid(WindowGuidString)]
    public class AgentChatToolWindowPane : ToolWindowPane
    {
        public const string WindowGuidString = "4dc5676e-51c3-4d43-9828-56eb0092c463";

        public AgentChatToolWindowPane() : base(null)
        {
            Caption = "Devin Agent Chat";
            Content = new AgentChatControl();
        }
    }
}
