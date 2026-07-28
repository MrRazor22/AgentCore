using CodeSharp.Tools;
using Xunit;

namespace CodeSharp.Tests;

public class ScheduleToolTests
{
    [Fact]
    public void Schedule_CreateListAndCancel_WorksLogically()
    {
        var tool = new ScheduleTool();

        // 1. Initial list empty
        var initialList = tool.Schedule("list");
        Assert.Contains("No active schedules", initialList);

        // 2. Create one-shot schedule
        var createResult = tool.Schedule("create", afterSeconds: 60, prompt: "Perform status check");
        Assert.Contains("created successfully", createResult);
        Assert.Contains("Schedule #1", createResult);

        // 3. List contains newly created schedule
        var listWithItem = tool.Schedule("list");
        Assert.Contains("#1", listWithItem);
        Assert.Contains("One-shot in 60s", listWithItem);
        Assert.Contains("Perform status check", listWithItem);

        // 4. Cancel schedule
        var cancelResult = tool.Schedule("cancel", scheduleId: 1);
        Assert.Contains("cancelled successfully", cancelResult);

        // 5. Verify list is empty again
        var finalList = tool.Schedule("list");
        Assert.Contains("No active schedules", finalList);
    }
}
