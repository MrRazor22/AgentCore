using System.Runtime.CompilerServices;
using System.Text;
using AgentCore.Conversation;
using AgentCore.LLM;
using AgentCore.Runtime;
using AgentCore.Tooling;

namespace AgentCore.CodingAgent;

public sealed partial class CodingAgent : IAgent
{
    private readonly string _name;
    private readonly string? _instructions;
    private readonly ILLMExecutor _llmExecutor;
    private readonly LLMOptions _llmOptions;
    private readonly IToolRegistry _toolRegistry;
    private readonly ICSharpExecutor _executor;
    private readonly SandboxPolicy _sandboxPolicy;
    private readonly int _maxSteps;
    private readonly (string open, string close) _codeBlockTags;
    private readonly IChatStore _memory;

    public string Name => _name;
    public string? Description => "A code-executing agent that generates C# code to solve tasks";

    public CodingAgent(
        string name,
        string? instructions,
        ILLMExecutor llmExecutor,
        LLMOptions llmOptions,
        IToolRegistry toolRegistry,
        ICSharpExecutor executor,
        SandboxPolicy sandboxPolicy,
        int maxSteps,
        (string open, string close) codeBlockTags,
        IChatStore memory,
        IToolExecutor toolExecutor)
    {
        _name = name;
        _instructions = instructions;
        _llmExecutor = llmExecutor;
        _llmOptions = llmOptions;
        _toolRegistry = toolRegistry;
        _executor = executor;
        _sandboxPolicy = sandboxPolicy;
        _maxSteps = maxSteps;
        _codeBlockTags = codeBlockTags;
        _memory = memory;

        var tools = toolRegistry.Tools;
        _executor.SendTools(tools, toolExecutor);
    }

    public async Task<AgentResponse> InvokeAsync(IContent input, string? sessionId = null, CancellationToken ct = default)
    {
        var events = new List<AgentEvent>();
        await foreach (var evt in InvokeStreamingAsync(input, sessionId, ct))
        {
            events.Add(evt);
        }

        var session = sessionId ?? Guid.NewGuid().ToString("N");
        var messages = await _memory.RecallAsync(session);
        
        return new AgentResponse(session, messages, AgentCore.Tokens.TokenUsage.Empty);
    }

    public async Task<T?> InvokeAsync<T>(IContent input, string? sessionId = null, CancellationToken ct = default)
    {
        var response = await InvokeAsync(input, sessionId, ct);
        return default;
    }

    public async IAsyncEnumerable<AgentEvent> InvokeStreamingAsync(
        IContent input,
        string? sessionId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var session = sessionId ?? Guid.NewGuid().ToString("N");

        var chat = await _memory.RecallAsync(session);
        var userMessage = new Message(Role.User, input);
        chat.Add(userMessage);

        yield return new AgentMessageEvent(userMessage);

        var systemPrompt = BuildSystemPrompt();
        var systemMessage = new Message(Role.System, new Text(systemPrompt));
        chat.Insert(0, systemMessage);

        var stepCount = 0;
        while (stepCount < _maxSteps)
        {
            stepCount++;

            var allMessages = chat.ToList();
            var options = new LLMOptions
            {
                Model = _llmOptions.Model,
                ApiKey = _llmOptions.ApiKey,
                BaseUrl = _llmOptions.BaseUrl,
                Temperature = _llmOptions.Temperature,
                MaxOutputTokens = _llmOptions.MaxOutputTokens,
                ToolCallMode = ToolCallMode.None,
                StopSequences = ["Observation:"],
            };

            var textBuffer = new StringBuilder();
            var reasoningBuffer = new StringBuilder();

            await foreach (var evt in _llmExecutor.StreamAsync(allMessages, options, ct))
            {
                switch (evt)
                {
                    case TextEvent t:
                        textBuffer.Append(t.Delta);
                        yield return evt;
                        break;
                    case ReasoningEvent r:
                        reasoningBuffer.Append(r.Delta);
                        yield return evt;
                        break;
                }
            }

            var llmOutput = textBuffer.ToString();
            var reasoning = reasoningBuffer.ToString();

            if (!string.IsNullOrEmpty(reasoning))
            {
                yield return new AgentReasoningEvent(reasoning);
            }

            if (!llmOutput.Trim().EndsWith(_codeBlockTags.close))
            {
                llmOutput += _codeBlockTags.close;
            }

            var assistantMessage = new Message(Role.Assistant, new Text(llmOutput));
            chat.Add(assistantMessage);

            yield return new AgentMessageEvent(assistantMessage);

            string codeAction = "";
            bool parseFailed = false;
            string parseError = "";
            
            try
            {
                codeAction = CodeParser.ParseCodeBlock(llmOutput, _codeBlockTags);
            }
            catch (CodeParseException ex)
            {
                parseFailed = true;
                parseError = ex.Message;
            }

            if (parseFailed)
            {
                yield return new CodeErrorEvent(llmOutput, parseError);
                var errorMsg = new Message(Role.User, new Text($"Error in code parsing: {parseError}"));
                chat.Add(errorMsg);
                yield return new AgentMessageEvent(errorMsg);
                continue;
            }

            yield return new CodeExecutionEvent(codeAction, new CodeOutput(null, "", false));

            var codeOutput = _executor.Execute(codeAction);

            yield return new CodeExecutionEvent(codeAction, codeOutput);

            var truncatedOutput = codeOutput.Output?.ToString() ?? "";
            if (truncatedOutput.Length > _sandboxPolicy.MaxOutputLength)
            {
                truncatedOutput = truncatedOutput[.._sandboxPolicy.MaxOutputLength] + "... (output truncated)";
            }

            var observation = $"Execution logs:\n{codeOutput.Logs}\nLast output from code snippet:\n{truncatedOutput}";
            var observationMessage = new Message(Role.User, new Text(observation));
            chat.Add(observationMessage);

            yield return new AgentMessageEvent(observationMessage);

            if (codeOutput.IsFinalAnswer)
            {
                yield return new AgentFinalResultEvent(codeOutput.Output);
                break;
            }
        }

        if (stepCount >= _maxSteps)
        {
            yield return new CodeErrorEvent("", $"Max steps ({_maxSteps}) exceeded");
        }
    }

    private string BuildSystemPrompt()
    {
        var prompt = CodingAgentPrompts.GetSystemPrompt(
            _instructions,
            _sandboxPolicy.AllowedNamespaces,
            _codeBlockTags);

        var tools = _toolRegistry.Tools;
        var toolPrompt = CodingAgentPrompts.GetToolPrompt(tools);

        return prompt + "\n\n" + toolPrompt;
    }
}


