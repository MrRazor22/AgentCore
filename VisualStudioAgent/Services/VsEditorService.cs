using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using VisualStudioAgent.Abstractions;

namespace VisualStudioAgent.Services;

public sealed class VsEditorService : IVsEditorService
{
    public async Task<string> ReadFileAsync(string path, int startLine, int endLine)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var dte = (DTE?)Package.GetGlobalService(typeof(DTE));

        string fullText;
        var doc = FindOpenDocument(dte, path);
        if (doc?.Object("TextDocument") is TextDocument textDoc)
        {
            var start = textDoc.CreateEditPoint();
            var end = textDoc.CreateEditPoint();
            end.EndOfDocument();
            fullText = start.GetText(end);
        }
        else if (File.Exists(path))
        {
            fullText = File.ReadAllText(path);
        }
        else
        {
            return $"Error: File '{path}' does not exist.";
        }

        if (string.IsNullOrEmpty(fullText)) return $"[File '{path}' is empty]";

        var allLines = fullText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        startLine = Math.Max(1, startLine);
        endLine = (endLine <= 0 || endLine < startLine) ? Math.Min(allLines.Length, startLine + 799) : Math.Min(allLines.Length, endLine);

        if (startLine > allLines.Length) return $"Error: startLine {startLine} exceeds total lines ({allLines.Length}).";

        int maxLineWidth = endLine.ToString().Length;
        var sb = new StringBuilder();
        for (int i = startLine - 1; i < endLine; i++)
            sb.Append((i + 1).ToString().PadLeft(maxLineWidth)).Append(": ").AppendLine(allLines[i]);

        string result = sb.ToString().TrimEnd();
        if (endLine < allLines.Length)
            result += $"\n\n[Content limited to lines {startLine}-{endLine} of {allLines.Length}. Continue with startLine={endLine + 1}.]";

        return result;
    }

    public async Task<string> EditFileAsync(string path, string targetContent, string replacementContent)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var dte = (DTE?)Package.GetGlobalService(typeof(DTE));

        bool undoOpened = false;
        if (dte?.UndoContext != null && !dte.UndoContext.IsOpen)
        {
            dte.UndoContext.Open("Devin Agent: Edit File");
            undoOpened = true;
        }

        try
        {
            var doc = FindOpenDocument(dte, path);
            if (doc == null && File.Exists(path) && dte?.ItemOperations != null)
            {
                try { doc = dte.ItemOperations.OpenFile(path)?.Document; } catch { }
            }

            if (doc?.Object("TextDocument") is TextDocument textDoc)
            {
                var start = textDoc.CreateEditPoint();
                var end = textDoc.CreateEditPoint();
                end.EndOfDocument();
                string updated = ApplyEdit(start.GetText(end), targetContent, replacementContent);
                start.ReplaceText(end, updated, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
                return $"Successfully updated '{path}' in Visual Studio editor buffer.";
            }

            if (!File.Exists(path) && !string.IsNullOrEmpty(targetContent))
                return $"Error: File '{path}' does not exist. Leave targetContent empty to create a new file.";

            string current = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            string diskUpdated = ApplyEdit(current, targetContent, replacementContent);

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, diskUpdated);

            if (dte?.ItemOperations != null)
            {
                try { dte.ItemOperations.OpenFile(path); } catch { }
            }

            return File.Exists(path) ? $"Successfully updated '{path}' on disk." : $"Successfully created '{path}'.";
        }
        finally
        {
            if (undoOpened && dte?.UndoContext != null && dte.UndoContext.IsOpen)
                dte.UndoContext.Close();
        }
    }

    private static Document? FindOpenDocument(DTE? dte, string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (dte?.Documents == null) return null;
        foreach (Document doc in dte.Documents)
            if (string.Equals(doc.FullName, path, StringComparison.OrdinalIgnoreCase)) return doc;
        return null;
    }

    private static string ApplyEdit(string text, string targetContent, string replacementContent)
    {
        if (string.IsNullOrEmpty(targetContent)) return replacementContent;

        int firstIdx = text.IndexOf(targetContent, StringComparison.Ordinal);
        if (firstIdx < 0)
            throw new InvalidOperationException("targetContent not found in file. Ensure exact character and whitespace match.");

        int secondIdx = text.IndexOf(targetContent, firstIdx + targetContent.Length, StringComparison.Ordinal);
        if (secondIdx >= 0)
            throw new InvalidOperationException("targetContent matched multiple locations in file. Provide more context to make the match unique.");

        return text.Remove(firstIdx, targetContent.Length).Insert(firstIdx, replacementContent);
    }
}
