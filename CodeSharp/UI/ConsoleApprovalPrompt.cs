using System.Net;
using System.Text.RegularExpressions;
using AgentCore.LLM.Chat;
using Spectre.Console;

namespace CodeSharp.UI;

/// <summary>
/// Spectre.Console implementation of IApprovalPrompt for interactive terminal confirmation.
/// Uses async confirmation with CancellationToken support and thread-safe console serialization.
/// </summary>
public sealed class ConsoleApprovalPrompt : Layers.IApprovalPrompt
{
    private static readonly SemaphoreSlim _promptLock = new(1, 1);

    public async Task<bool> RequestApprovalAsync(ToolCall call, CancellationToken ct)
    {
        await _promptLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var args = call.Arguments?.ToString() ?? "{}";
            var truncatedArgs = args.Length > 200 ? args[..200] + "..." : args;

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold yellow]⚠ Approval Required:[/] [bold white]{Markup.Escape(call.Name)}[/]");
            AnsiConsole.MarkupLine($"  [grey]Arguments:[/] {Markup.Escape(truncatedArgs)}");

            return await AnsiConsole.ConfirmAsync(
                "  [bold green]Allow execution?[/]",
                defaultValue: false,
                cancellationToken: ct).ConfigureAwait(false);
        }
        finally
        {
            _promptLock.Release();
        }
    }
}
