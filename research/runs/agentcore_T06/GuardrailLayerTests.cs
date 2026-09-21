using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using Xunit;

namespace AgentCoreT06;

public class GuardrailLayerTests
{
    private sealed class MockLLM : ILLM
    {
        public int CallCount { get; private set; }

        public async IAsyncEnumerable<IMessageEvent> GenerateAsync(
            IReadOnlyList<Message> messages,
            IReadOnlyList<ToolDefinition>? tools = null,
            JsonSchema? responseSchema = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            CallCount++;
            yield return new MessageDelta("m1", Content: new TextDelta(0, "Safe response"));
            await Task.Yield();
        }
    }

    private sealed class MockToolbox : IToolbox
    {
        public int CallCount { get; private set; }

        public ValueTask<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<ITool>>([]);

        public async IAsyncEnumerable<IMessageEvent> ExecuteAsync(
            IReadOnlyList<ToolCall> calls,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            CallCount++;
            yield return new MessageDelta(calls[0].Id, Content: new ToolResult(calls[0].Id, [new Text("Tool output")]));
            await Task.Yield();
        }
    }

    [Fact]
    public async Task Test1_RejectsPromptMatchingProhibitedPatternWithValidationException()
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var mock = new MockLLM();
        var layer = new InputGuardrailLLMLayer(
            promptValidator: text => text.Contains("PROHIBITED_INJECTION")
                ? (false, "PromptSafetyRule", "Prompt contains prohibited injection pattern")
                : (true, "", ""),
            onDiagnostic: d => diagnostics.Add(d),
            inner: mock);

        var messages = new List<Message> { new(Role.User, [new Text("Execute PROHIBITED_INJECTION attack")]) };

        var ex = await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await foreach (var _ in layer.GenerateAsync(messages)) { }
        });

        Assert.Equal(0, mock.CallCount); // LLM was NEVER called
        Assert.Equal("PromptSafetyRule", ex.Diagnostic.RuleName);
        Assert.Single(diagnostics);
        Assert.Equal("Prompt", diagnostics[0].Target);
    }

    [Fact]
    public async Task Test2_ValidatesToolArgumentsAgainstConstraintsPriorToInvocation()
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var mock = new MockToolbox();
        var layer = new ToolArgumentGuardrailLayer(
            toolValidator: call =>
            {
                if (call.Name == "transfer_funds" && call.Arguments.Contains("\"amount\": -100"))
                {
                    return (false, "PositiveAmountRule", "Transfer amount must be non-negative");
                }
                return (true, "", "");
            },
            onDiagnostic: d => diagnostics.Add(d),
            inner: mock);

        var calls = new List<ToolCall> { new("c1", "transfer_funds", "{\"amount\": -100}") };

        var ex = await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await foreach (var _ in layer.ExecuteAsync(calls)) { }
        });

        Assert.Equal(0, mock.CallCount); // Toolbox was NEVER called
        Assert.Equal("PositiveAmountRule", ex.Diagnostic.RuleName);
        Assert.Equal("transfer_funds", ex.Diagnostic.Target);
        Assert.Single(diagnostics);
    }

    [Fact]
    public async Task Test3_AllowsBenignInputsToProceedUnmodified()
    {
        var mock = new MockLLM();
        var layer = new InputGuardrailLLMLayer(
            promptValidator: text => text.Contains("MALICIOUS")
                ? (false, "Rule", "Bad")
                : (true, "", ""),
            inner: mock);

        var messages = new List<Message> { new(Role.User, [new Text("What is the weather today?")]) };
        var results = new List<IMessageEvent>();

        await foreach (var evt in layer.GenerateAsync(messages))
        {
            results.Add(evt);
        }

        Assert.Equal(1, mock.CallCount);
        Assert.Single(results);
    }

    [Fact]
    public async Task Test4_EmitsStructuredValidationFailureDiagnostics()
    {
        ValidationDiagnostic? captured = null;
        var mock = new MockLLM();
        var layer = new InputGuardrailLLMLayer(
            promptValidator: text => (false, "StrictSafetyPolicy", "Disallowed topic"),
            onDiagnostic: d => captured = d,
            inner: mock);

        var messages = new List<Message> { new(Role.User, [new Text("Restricted query")]) };

        var ex = await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await foreach (var _ in layer.GenerateAsync(messages)) { }
        });

        Assert.NotNull(captured);
        Assert.Equal("Prompt", captured.Target);
        Assert.Equal("StrictSafetyPolicy", captured.RuleName);
        Assert.Equal("Restricted query", captured.ViolatingValue);
        Assert.Equal("Disallowed topic", captured.Reason);
        Assert.True(captured.Timestamp <= DateTimeOffset.UtcNow);
    }
}
