using Spectre.Console;

namespace CodeSharp.UI;

/// <summary>
/// Reusable interactive menu helper built on Spectre SelectionPrompt.
/// Supports clean, minimalist selection menus across the application (tool approvals, session menus, help, reverts).
/// </summary>
public static class MenuPrompt
{
    /// <summary>
    /// Displays a minimal, styled interactive selection menu with arrow-key navigation.
    /// </summary>
    public static async Task<T> SelectAsync<T>(
        string title,
        IEnumerable<T> choices,
        Func<T, string>? displaySelector = null,
        CancellationToken ct = default) where T : notnull
    {
        var prompt = new SelectionPrompt<T>()
            .Title(title)
            .PageSize(10)
            .HighlightStyle(new Style(Color.Cyan, decoration: Decoration.Bold));

        if (displaySelector != null)
        {
            prompt.UseConverter(displaySelector);
        }

        foreach (var choice in choices)
        {
            prompt.AddChoice(choice);
        }

        return await prompt.ShowAsync(AnsiConsole.Console, ct).ConfigureAwait(false);
    }
}
