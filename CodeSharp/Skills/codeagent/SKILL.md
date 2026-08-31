---
name: codeagent
description: PowerShell workflows for searching, reading, editing and safe file operations.
---

# CodeAgent: PowerShell File Operations & Workflows

This skill provides bulletproof patterns and workflows for file discovery, inspection, modification, and execution in PowerShell. Follow these rules to avoid common pitfalls like encoding corruption, variable expansion errors, escaping bugs, and hanging commands.

---

## 1. Golden Rules & Best Practices

1. **Always use UTF-8**: Always pass `-Encoding utf8` to both `Get-Content` and `Set-Content` / `Out-File`.
2. **Always use Verbatim Here-Strings (`@' ... '@`) for writing files**: Double-quoted strings (`@" ... "@`) expand PowerShell variables (`$foo`), backticks, and subexpressions. Single-quoted here-strings (`@' ... '@`) write raw code verbatim without any unwanted substitutions.
3. **Ensure parent directories exist**: Before creating a new file in a subfolder, ensure the directory exists using `New-Item -ItemType Directory -Path <dir> -Force | Out-Null`.
4. **Never block on interactive prompts**: Always specify `-Force` or `-Confirm:$false` for operations that might ask for confirmation (e.g. `Remove-Item`, `New-Item`).
5. **Always quote paths**: Wrap paths in double quotes (e.g. `-Path "src/My Project/File.cs"`) to handle spaces and special characters safely.

---

## 2. Searching & Discovery

### A. List Directory Structure
Inspect directory trees with controlled depth to avoid overwhelming outputs:
```powershell
# List current directory up to 2 levels deep
Get-ChildItem -Path . -Depth 2 | Select-Object FullName, Length, Mode
```

### B. Find Files by Glob / Pattern
Search for files across the workspace:
```powershell
# Find all .cs files recursively
Get-ChildItem -Path . -Filter "*.cs" -Recurse -File | Select-Object -ExpandProperty FullName

# Find files matching multiple extensions
Get-ChildItem -Path . -Recurse -File -Include "*.json","*.xml","*.config"
```

### C. Search File Contents (Grep)
Search for keywords or regex patterns inside files:
```powershell
# Search for pattern across all .cs files
Get-ChildItem -Path . -Filter "*.cs" -Recurse -File | Select-String -Pattern "class\s+\w+"

# Search with line numbers and case-insensitive matching
Select-String -Path "src/**/*.cs" -Pattern "TODO" -CaseSensitive:$false
```

---

## 3. Reading & Inspecting Files

### A. Read Full Content
```powershell
Get-Content -Path "src/App.cs" -Raw -Encoding utf8
```

### B. Read Line Slices / Ranges
When inspecting large files, read only the relevant line range:
```powershell
# Read first 50 lines
Get-Content -Path "src/App.cs" -TotalCount 50 -Encoding utf8

# Read lines 100 to 150 (skip 99, take 50)
Get-Content -Path "src/App.cs" -Encoding utf8 | Select-Object -Skip 99 -First 50

# Read with line numbers
Get-Content -Path "src/App.cs" -Encoding utf8 | ForEach-Object { "$($_.ReadCount): $_" }
```

### C. Check Existence
```powershell
Test-Path -Path "src/App.cs" -PathType Leaf
```

---

## 4. Creating & Writing Files

### A. Create or Overwrite a File with Here-String
Use single-quoted here-strings (`@' ... '@`) to write code files cleanly without variable mangling:
```powershell
$target = "src/Services/Greeter.cs"
$dir = Split-Path $target
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

Set-Content -Path $target -Value @'
using System;

namespace MyProject.Services;

public sealed class Greeter
{
    public string Greet(string name) => $"Hello, {name}!";
}
'@ -Encoding utf8
```

### B. Append to a File
```powershell
Add-Content -Path "logs.txt" -Value "Build finished successfully." -Encoding utf8
```

---

## 5. Editing & Modifying Files

### A. Exact Text Replacement
For targeted edits, read as raw UTF-8, replace exact text, and write back:
```powershell
$file = "src/Services/Greeter.cs"
$content = Get-Content -Path $file -Raw -Encoding utf8
$content = $content.Replace("Hello, {name}!", "Welcome, {name}!")
Set-Content -Path $file -Value $content -Encoding utf8
```

### B. Regex Replacement
```powershell
$file = "src/Services/Greeter.cs"
$content = Get-Content -Path $file -Raw -Encoding utf8
$content = $content -replace 'public\s+string\s+Greet', 'public static string Greet'
Set-Content -Path $file -Value $content -Encoding utf8
```

---

## 6. Deleting & Cleaning

### A. Delete a File
```powershell
if (Test-Path "temp.txt") {
    Remove-Item -Path "temp.txt" -Force
}
```

### B. Delete a Directory Recursively
```powershell
if (Test-Path "bin") {
    Remove-Item -Path "bin" -Recurse -Force
}
```

---

## 7. Common Pitfalls to Avoid

* ❌ **DO NOT use double-quoted here-strings (`@" ... "@`) for source code**: If code contains `$interpolated` variables or `$"strings"`, PowerShell will attempt to evaluate them before writing to disk. Always use `@' ... '@`.
* ❌ **DO NOT omit `-Encoding utf8`**: Default PowerShell encoding might use Windows-1252 or UTF-16 LE, which breaks cross-platform tools and git diffs.
* ❌ **DO NOT run commands that prompt for user confirmation without `-Force`**.
