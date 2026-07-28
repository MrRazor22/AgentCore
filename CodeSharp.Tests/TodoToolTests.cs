using CodeSharp.Tools;
using Xunit;

namespace CodeSharp.Tests;

public class TodoToolTests
{
    [Fact]
    public void TodoList_MaintainsTasksCorrectly()
    {
        var tool = new TodoTool();

        // 1. Initial empty state
        var resultEmpty = tool.TodoList();
        Assert.Contains("empty", resultEmpty);

        // 2. Set new todos
        var tasks = new[] { "Implement unit tests", "Verify all pass" };
        var resultAdded = tool.TodoList(tasks);

        Assert.Contains("1. [ ] Implement unit tests", resultAdded);
        Assert.Contains("2. [ ] Verify all pass", resultAdded);

        // 3. Get existing todos
        var resultGet = tool.TodoList();
        Assert.Contains("1. [ ] Implement unit tests", resultGet);
    }
}
