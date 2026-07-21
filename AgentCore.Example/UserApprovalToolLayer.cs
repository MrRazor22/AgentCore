using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;
using AgentCore.Tools;

namespace AgentCore.Example;

/// <summary>
/// A decorator layer for IToolService that prompts the user in the console
/// for permission before executing sensitive actions (e.g. WriteTextFile).
/// </summary>
public class UserApprovalToolLayer : IToolService
{
    private readonly IToolService _inner;

    public UserApprovalToolLayer(IToolService inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public IReadOnlyList<Tool> Tools => _inner.Tools;

    public async Task<IReadOnlyList<Message>> ExecuteAsync(IEnumerable<ToolCall> calls, CancellationToken ct = default)
    {
        var approvedCalls = new List<ToolCall>();
        var responses = new List<Message>();

        foreach (var call in calls)
        {
            if (call.Name.Equals("WriteTextFile", StringComparison.OrdinalIgnoreCase))
            {
                // Retrieve the path from JSON arguments safely
                var path = "unknown";
                if (call.ArgumentsObject != null && call.ArgumentsObject.TryGetPropertyValue("path", out var pathNode))
                {
                    path = pathNode?.ToString() ?? "unknown";
                }

                // Prompt user for confirmation on console
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"\n[Security Alert] The Agent wants to write content to the file: '{path}'.\nDo you want to allow this action? (y/n): ");
                Console.ResetColor();

                var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (answer == "y" || answer == "yes")
                {
                    approvedCalls.Add(call);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[Access Denied] Write operation blocked by user for '{path}'.");
                    Console.ResetColor();

                    // Inject rejection message to let the workflow know the tool failed due to lack of permission
                    var rejectionResult = new ToolResult(call.Id, new Text("Error: Permission denied. User rejected the request to write to file."));
                    responses.Add(new Message(Role.Tool, rejectionResult));
                }
            }
            else if (call.Name.Equals("ExecuteCommand", StringComparison.OrdinalIgnoreCase))
            {
                var command = "unknown";
                if (call.ArgumentsObject != null && call.ArgumentsObject.TryGetPropertyValue("command", out var cmdNode))
                {
                    command = cmdNode?.ToString() ?? "unknown";
                }

                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"\n[Security Alert] The Agent wants to execute command: '{command}'.\nDo you want to allow this action? (y/n): ");
                Console.ResetColor();

                var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (answer == "y" || answer == "yes")
                {
                    approvedCalls.Add(call);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[Access Denied] Command execution blocked by user: '{command}'.");
                    Console.ResetColor();

                    var rejectionResult = new ToolResult(call.Id, new Text("Error: Permission denied. User rejected the request to execute this command."));
                    responses.Add(new Message(Role.Tool, rejectionResult));
                }
            }
            else
            {
                // Automatic approval for reading/math/weather tools
                approvedCalls.Add(call);
            }
        }

        if (approvedCalls.Count > 0)
        {
            var innerResults = await _inner.ExecuteAsync(approvedCalls, ct);
            responses.AddRange(innerResults);
        }

        return responses;
    }
}
