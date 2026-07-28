using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using AgentCore.LLM.Chat;
using CodeSharp.UI;
using Xunit;

namespace CodeSharp.Tests;

public class ToolDisplayFormatterTests
{
    [Fact]
    public void RunCommandFormatter_ExtractsCommandLineUntruncated()
    {
        var formatter = new RunCommandFormatter();
        var cmd = "dotnet test --filter FullyQualifiedName=SomeLongNamespace.SomeLongClassName.SomeLongMethodName";
        var call = new ToolCall("1", "RunCommand", new JsonObject
        {
            ["commandLine"] = cmd,
            ["cwd"] = "src"
        });

        Assert.True(formatter.CanFormat("RunCommand"));
        var summary = formatter.FormatCall(call);

        Assert.Equal("Run command?", summary.DisplayName);
        Assert.Equal(cmd + " in src", summary.ArgSummary);
        Assert.Null(summary.LongDetails);
    }

    [Fact]
    public void EditFileFormatter_ExtractsMetadataAndPreservesDetails()
    {
        var formatter = new EditFileFormatter();
        var content = "public void Hello() {\n  Console.WriteLine(\"Hi\");\n}";
        var call = new ToolCall("1", "EditFile", new JsonObject
        {
            ["filePath"] = "src/Helper.cs",
            ["replacementContent"] = content
        });

        Assert.True(formatter.CanFormat("EditFile"));
        var summary = formatter.FormatCall(call);

        Assert.Equal("Create file?", summary.DisplayName);
        Assert.Contains("src/Helper.cs", summary.ArgSummary);
        Assert.Equal(content, summary.LongDetails);
    }

    [Fact]
    public void CompositeToolDisplayFormatter_FallsBackToGenericFormatter()
    {
        var formatter = new CompositeToolDisplayFormatter(new[] { new RunCommandFormatter() });
        var call = new ToolCall("1", "CustomTool", new JsonObject
        {
            ["param1"] = "val1"
        });

        var summary = formatter.FormatCall(call);

        Assert.Equal("Execute tool 'CustomTool'?", summary.DisplayName);
        Assert.Contains("param1", summary.ArgSummary);
        Assert.Contains("val1", summary.LongDetails);
    }
}
