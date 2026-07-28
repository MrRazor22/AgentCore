using System;
using System.IO;
using System.Security;
using CodeSharp.Tools;
using Xunit;

namespace CodeSharp.Tests;

public class FileToolsTests : IDisposable
{
    private readonly string _tempWorkspace;

    public FileToolsTests()
    {
        _tempWorkspace = Path.Combine(Path.GetTempPath(), "FileToolsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempWorkspace);
        FileTools.Initialize(_tempWorkspace);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempWorkspace))
            {
                Directory.Delete(_tempWorkspace, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void PathGuard_EnforcesWorkspaceBoundary_ThrowsSecurityExceptionOnEscape()
    {
        // 1. Traverse up escaping workspace
        Assert.Throws<SecurityException>(() =>
        {
            FileTools.EditFile("../escaped.txt", "", "hello");
        });

        // 2. Absolute path outside workspace
        var tempOutside = Path.GetTempPath();
        Assert.Throws<SecurityException>(() =>
        {
            FileTools.EditFile(Path.Combine(tempOutside, "escaped.txt"), "", "hello");
        });
    }

    [Fact]
    public void EditFile_CreateNewFile_Succeeds()
    {
        var relativePath = "sub/newfile.txt";
        var content = "This is a new file.";
        
        var result = FileTools.EditFile(relativePath, targetContent: "", replacementContent: content);

        Assert.Contains("Successfully created file", result);
        
        var fullPath = Path.Combine(_tempWorkspace, relativePath);
        Assert.True(File.Exists(fullPath));
        Assert.Equal(content, File.ReadAllText(fullPath));
    }

    [Fact]
    public void EditFile_ReplaceContent_Succeeds()
    {
        var relativePath = "change.txt";
        var fullPath = Path.Combine(_tempWorkspace, relativePath);
        File.WriteAllText(fullPath, "line 1\nline 2\nline 3");

        var result = FileTools.EditFile(relativePath, targetContent: "line 2", replacementContent: "replaced line 2");

        Assert.Contains("Successfully updated", result);
        Assert.Equal("line 1\nreplaced line 2\nline 3", File.ReadAllText(fullPath));
    }

    [Fact]
    public void EditFile_DuplicateMatches_ReturnsError()
    {
        var relativePath = "duplicate.txt";
        var fullPath = Path.Combine(_tempWorkspace, relativePath);
        File.WriteAllText(fullPath, "same\nsame\ndifferent");

        var result = FileTools.EditFile(relativePath, targetContent: "same", replacementContent: "replaced");

        Assert.Contains("matched multiple locations", result);
        Assert.Equal("same\nsame\ndifferent", File.ReadAllText(fullPath)); // unchanged
    }

    [Fact]
    public void Filesystem_MoveCopyDelete_FollowsContract()
    {
        var srcRel = "src.txt";
        var srcFull = Path.Combine(_tempWorkspace, srcRel);
        File.WriteAllText(srcFull, "source content");

        // 1. Copy
        var destRel = "dest.txt";
        var copyResult = FileTools.Filesystem(action: "copy", path: srcRel, destination: destRel);
        Assert.Contains("Copied file", copyResult);
        Assert.True(File.Exists(Path.Combine(_tempWorkspace, destRel)));

        // 2. Move
        var movedRel = "moved.txt";
        var moveResult = FileTools.Filesystem(action: "move", path: destRel, destination: movedRel);
        Assert.Contains("Moved file", moveResult);
        Assert.True(File.Exists(Path.Combine(_tempWorkspace, movedRel)));
        Assert.False(File.Exists(Path.Combine(_tempWorkspace, destRel)));

        // 3. Delete
        var deleteResult = FileTools.Filesystem(action: "delete", path: movedRel);
        Assert.Contains("Deleted file", deleteResult);
        Assert.False(File.Exists(Path.Combine(_tempWorkspace, movedRel)));
    }
}
