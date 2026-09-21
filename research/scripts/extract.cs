using System.Text;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
var outDir = Path.Combine(root, "_ai_context");
Directory.CreateDirectory(outDir);

var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "bin", "obj", ".vs", ".git", ".vscode", ".idea", "TestResults", "node_modules", "packages", "dist", "build", "_ai_context" };
var excludedPatterns = new[] { ".Designer.cs", ".g.cs", ".g.i.cs", ".Generated.cs", ".AssemblyAttributes.cs", "AssemblyInfo.cs", "GlobalUsings.g.cs" };

bool IsIgnored(string name) =>
    name is "combined_code.cs" or "project_structure.md" or "solution_context.md" or "extract.cs" ||
    excludedPatterns.Any(p => name.EndsWith(p, StringComparison.OrdinalIgnoreCase));

bool IsTest(string path) => path.Contains("test", StringComparison.OrdinalIgnoreCase);

// 1. Discover projects
var projectFiles = Directory.EnumerateFiles(root, "*.*proj", SearchOption.AllDirectories)
    .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(excludedDirs.Contains))
    .OrderBy(Path.GetFileNameWithoutExtension).ToList();

var targetDirs = new List<(string Name, string Path)>();
if (projectFiles.Count > 0)
{
    Console.WriteLine("Discovered Projects:\n  [A] All (exclude tests)\n  [T] All (include tests)");
    for (var i = 0; i < projectFiles.Count; i++)
    {
        var name = Path.GetFileNameWithoutExtension(projectFiles[i]);
        var relDir = Path.GetRelativePath(root, Path.GetDirectoryName(projectFiles[i])!).Replace('\\', '/');
        Console.WriteLine($"  [{i + 1}] {name} ({relDir})");
    }

    Console.Write("\nSelect (numbers, 'A', or 'T') [A]: ");
    var input = Console.ReadLine()?.Trim() ?? "A";
    if (string.IsNullOrEmpty(input) || input.Equals("A", StringComparison.OrdinalIgnoreCase))
        targetDirs.AddRange(projectFiles.Where(p => !IsTest(p)).Select(p => (Path.GetFileNameWithoutExtension(p), Path.GetDirectoryName(p)!)));
    else if (input.Equals("T", StringComparison.OrdinalIgnoreCase))
        targetDirs.AddRange(projectFiles.Select(p => (Path.GetFileNameWithoutExtension(p), Path.GetDirectoryName(p)!)));
    else
    {
        var indices = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var idx) ? idx - 1 : -1)
            .Where(idx => idx >= 0 && idx < projectFiles.Count).Distinct();
        foreach (var idx in indices)
            targetDirs.Add((Path.GetFileNameWithoutExtension(projectFiles[idx]), Path.GetDirectoryName(projectFiles[idx])!));
    }
}
if (targetDirs.Count == 0) targetDirs.Add((new DirectoryInfo(root).Name, root));

// 2. Scan & collect files
var csFiles = targetDirs
    .SelectMany(t => Directory.EnumerateFiles(t.Path, "*.cs", SearchOption.AllDirectories)
        .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(excludedDirs.Contains) && !IsIgnored(Path.GetFileName(p)))
        .Select(p => (Project: t.Name, Path: p)))
    .DistinctBy(f => f.Path).OrderBy(f => f.Path).ToList();

// 3. Tree generator
void AppendTree(StringBuilder sb, DirectoryInfo dir, string indent)
{
    var entries = dir.GetFileSystemInfos()
        .Where(e => e is DirectoryInfo d ? !excludedDirs.Contains(d.Name) : !IsIgnored(e.Name))
        .OrderBy(e => e is FileInfo).ThenBy(e => e.Name).ToList();

    for (var i = 0; i < entries.Count; i++)
    {
        var isLast = i == entries.Count - 1;
        var marker = isLast ? "└── " : "├── ";
        var nextIndent = indent + (isLast ? "    " : "│   ");
        if (entries[i] is DirectoryInfo sub)
        {
            sb.AppendLine($"{indent}{marker}{sub.Name}/");
            AppendTree(sb, sub, nextIndent);
        }
        else sb.AppendLine($"{indent}{marker}{entries[i].Name}");
    }
}

var rootDir = new DirectoryInfo(root);
var treeSb = new StringBuilder($"# Project Structure\n\n```text\n{rootDir.Name}/\n");
if (targetDirs.Count == 1 && targetDirs[0].Path == root) AppendTree(treeSb, rootDir, "");
else foreach (var t in targetDirs) { treeSb.AppendLine($"├── {t.Name}/"); AppendTree(treeSb, new DirectoryInfo(t.Path), "│   "); }
treeSb.AppendLine("```");

// 4. Build output bundles with accurate code vs total line count
var codeSb = new StringBuilder();
var contextSb = new StringBuilder();
var projectStats = new Dictionary<string, (int Files, int CodeLines, int TotalLines)>();

foreach (var file in csFiles)
{
    var rel = Path.GetRelativePath(root, file.Path).Replace('\\', '/');
    var text = await File.ReadAllTextAsync(file.Path);
    var lines = text.Split('\n');
    var totalLines = lines.Length - (text.EndsWith('\n') ? 1 : 0);
    var codeLines = lines.Count(l => !string.IsNullOrWhiteSpace(l));

    var cur = projectStats.GetValueOrDefault(file.Project);
    projectStats[file.Project] = (cur.Files + 1, cur.CodeLines + codeLines, cur.TotalLines + totalLines);

    codeSb.AppendLine($"// {new string('=', 76)}\n// FILE: {rel} ({codeLines} code lines, {totalLines} total)\n// {new string('=', 76)}\n\n{text}\n");
    contextSb.AppendLine($"### File: `{rel}` ({codeLines} code lines, {totalLines} total)\n```csharp\n{text}\n```\n");
}

var summarySb = new StringBuilder("# Solution Context\n\n> Complete codebase context and project structure for AI agent assistance.\n\n## Line Count Breakdown\n\n| Project | Files | Code Lines | Total Lines |\n|---|---|---|---|\n");
Console.WriteLine("\nLine Count Breakdown:");
var (totCode, totTotal) = (0, 0);
foreach (var (proj, stat) in projectStats)
{
    totCode += stat.CodeLines;
    totTotal += stat.TotalLines;
    summarySb.AppendLine($"| {proj} | {stat.Files} | {stat.CodeLines:N0} | {stat.TotalLines:N0} |");
    Console.WriteLine($"  {proj,-25} : {stat.Files,3} files | {stat.CodeLines,6:N0} code lines ({stat.TotalLines:N0} total)");
}
summarySb.AppendLine($"| **Total** | **{csFiles.Count}** | **{totCode:N0}** | **{totTotal:N0}** |\n\n");
Console.WriteLine($"  {"Total",-25} : {csFiles.Count,3} files | {totCode,6:N0} code lines ({totTotal:N0} total)\n");

contextSb.Insert(0, summarySb.ToString() + treeSb.ToString() + "\n## Source Code\n\n");

await File.WriteAllTextAsync(Path.Combine(outDir, "project_structure.md"), treeSb.ToString());
await File.WriteAllTextAsync(Path.Combine(outDir, "combined_code.cs"), codeSb.ToString());
await File.WriteAllTextAsync(Path.Combine(outDir, "solution_context.md"), contextSb.ToString());

Console.WriteLine($"Outputs saved in: {outDir}");
