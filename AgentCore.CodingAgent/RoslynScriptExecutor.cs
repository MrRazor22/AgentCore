using System.Text;
using AgentCore.Conversation;
using AgentCore.Tooling;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace AgentCore.CodingAgent;

public sealed class FinalAnswerException : Exception
{
    public object? Value { get; }

    public FinalAnswerException(object? value) : base("FinalAnswer")
    {
        Value = value;
    }
}

public sealed class RoslynScriptExecutor : ICSharpExecutor
{
    private readonly SandboxPolicy _policy;
    private readonly ScriptOptions _scriptOptions;
    private ScriptState<object>? _scriptState;
    private readonly StringBuilder _logs = new();
    private Dictionary<string, Delegate> _toolDelegates = [];
    private IToolExecutor? _toolExecutor;
    private IReadOnlyList<Tool>? _tools;

    public RoslynScriptExecutor(SandboxPolicy? policy = null)
    {
        _policy = policy ?? SandboxPolicy.Restrictive;
        _scriptOptions = CreateScriptOptions();
    }

    private ScriptOptions CreateScriptOptions()
    {
        var options = ScriptOptions.Default;

        if (_policy.AllowedNamespaces.Count > 0 && !_policy.AllowedNamespaces.Contains("*"))
        {
            options = options.WithImports(_policy.AllowedNamespaces);
        }

        return options;
    }

    public void SendTools(IReadOnlyList<Tool> tools, IToolExecutor executor)
    {
        _tools = tools;
        _toolExecutor = executor;
        _toolDelegates = ToolBridge.CreateToolDelegates(tools, executor);
    }

    public void SendVariables(Dictionary<string, object?> variables)
    {
        var globals = new ScriptGlobals
        {
            Print = (Action<object?>)((obj) => _logs.AppendLine(obj?.ToString() ?? "null")),
            FinalAnswer = (Action<object?>)((obj) => throw new FinalAnswerException(obj)),
            Tools = _toolDelegates,
            Variables = variables,
        };

        if (_scriptState == null)
        {
            _scriptState = CSharpScript.RunAsync<object>(
                "",
                _scriptOptions,
                globals,
                typeof(ScriptGlobals),
                default).Result;
        }
        else
        {
            _scriptState = _scriptState.ContinueWithAsync<object>(
                "",
                _scriptOptions,
                default).Result;
        }
    }

    public CodeOutput Execute(string codeAction)
    {
        _logs.Clear();
        var cts = new CancellationTokenSource(_policy.ExecutionTimeout);

        try
        {
            var globals = new ScriptGlobals
            {
                Print = (Action<object?>)((obj) =>
                {
                    var output = obj?.ToString() ?? "null";
                    if (_logs.Length + output.Length > _policy.MaxOutputLength)
                    {
                        output = output[..(_policy.MaxOutputLength - _logs.Length)];
                    }
                    _logs.AppendLine(output);
                }),
                FinalAnswer = (Action<object?>)((obj) => throw new FinalAnswerException(obj)),
                Tools = _toolDelegates,
                Variables = [],
            };

            var task = _scriptState == null
                ? CSharpScript.RunAsync<object>(codeAction, _scriptOptions, globals, typeof(ScriptGlobals), cts.Token)
                : _scriptState.ContinueWithAsync<object>(codeAction, _scriptOptions, cts.Token);

            _scriptState = task.Result;

            var output = _scriptState?.ReturnValue;
            var logs = TruncateLogs(_logs.ToString());

            return new CodeOutput(output, logs, false);
        }
        catch (FinalAnswerException ex)
        {
            var logs = TruncateLogs(_logs.ToString());
            return new CodeOutput(ex.Value, logs, true);
        }
        catch (CompilationErrorException ex)
        {
            var errorMsg = $"Compilation error:\n{string.Join("\n", ex.Diagnostics)}";
            return new CodeOutput(null, _logs.ToString() + errorMsg, false);
        }
        catch (Exception ex)
        {
            var errorMsg = $"Execution error: {ex.Message}";
            return new CodeOutput(null, _logs.ToString() + errorMsg, false);
        }
    }

    private string TruncateLogs(string logs)
    {
        if (logs.Length > _policy.MaxOutputLength)
        {
            return logs[.._policy.MaxOutputLength] + "\n... (output truncated)";
        }
        return logs;
    }

    public void Dispose()
    {
    }
}

public class ScriptGlobals
{
    public required Action<object?> Print { get; init; }
    public required Action<object?> FinalAnswer { get; init; }
    public required Dictionary<string, Delegate> Tools { get; init; }
    public required Dictionary<string, object?> Variables { get; init; }
}
