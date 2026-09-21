// ============================================================================
// FILE: AgentCore.Layers/Chat/ChatPersistenceLayer.cs (95 code lines, 104 total)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.Context;
using AgentCore.Context.Primitives;
using AgentCore.Layers.Context.Store;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Context;

public sealed class ChatPersistenceLayer(
    IChatStore store,
    IWalStore? walStore = null,
    IContext? inner = null) : ContextLayer(inner)
{
    private readonly IChatStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _restored;

    public override async Task<IReadOnlyList<Message>> ReadAsync(CancellationToken ct = default)
    {
        await RestoreAsync(ct).ConfigureAwait(false);
        return await base.ReadAsync(ct).ConfigureAwait(false);
    }

    public override async IAsyncEnumerable<IMessageEvent> WriteAsync(
        IAsyncEnumerable<IMessageEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await RestoreAsync(ct).ConfigureAwait(false);
        int initial = (await Inner.ReadAsync(ct: CancellationToken.None).ConfigureAwait(false)).Count;
        try
        {
            var source = walStore == null ? events : LogAsync();
            async IAsyncEnumerable<IMessageEvent> LogAsync()
            {
                await foreach (var evt in events.WithCancellation(ct).ConfigureAwait(false))
                {
                    await walStore.AppendAsync(evt, ct).ConfigureAwait(false);
                    yield return evt;
                }
            }

            await foreach (var evt in base.WriteAsync(source, ct).WithCancellation(ct).ConfigureAwait(false))
                yield return evt;
        }
        finally
        {
            try
            {
                var history = await Inner.ReadAsync(ct: CancellationToken.None).ConfigureAwait(false);
                var newMessages = history.Skip(initial).Where(m => m.Contents.Count > 0).ToList();
                if (newMessages.Count > 0)
                    await _store.AppendAsync(newMessages, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                if (walStore != null) await walStore.ClearAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task RestoreAsync(CancellationToken ct)
    {
        if (_restored) return;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_restored) return;

            if (walStore != null)
            {
                var recovered = await walStore.RecoverAsync(ct).ToMessagesAsync(ct: ct).ConfigureAwait(false);
                if (recovered.Count > 0) await _store.AppendAsync(recovered, ct).ConfigureAwait(false);
                await walStore.ClearAsync(ct).ConfigureAwait(false);
            }

            if (await _store.LoadAsync(ct).ConfigureAwait(false) is { Count: > 0 } history)
            {
                foreach (var m in ExtractWorkingContext(history))
                    await Inner.WriteAsync(m, ct).ConfigureAwait(false);
            }
            _restored = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static IReadOnlyList<Message> ExtractWorkingContext(IReadOnlyList<Message> history)
    {
        for (int i = history.Count - 1; i >= 0; i--)
            if (history[i].Get<Summary>() != null)
            {
                var sys = history.FirstOrDefault(m => m.Role == Role.System);
                return sys != null ? [sys, .. history.Skip(i)] : [.. history.Skip(i)];
            }
        return history;
    }
}


// ============================================================================
// FILE: AgentCore.Layers/Chat/ContextLayerExtensions.cs (44 code lines, 49 total)
// ============================================================================

using AgentCore;
using AgentCore.Context;
using AgentCore.Layers.Context.Store;

namespace AgentCore.Layers.Context;

public static class ContextLayerExtensions
{
    public static IContext UseSession(
        this IContext context,
        string storageDirectory,
        string sessionId,
        bool enableWal = true)
        => context.UseSession(
            new FileChatStore(storageDirectory ?? throw new ArgumentNullException(nameof(storageDirectory)), sessionId ?? throw new ArgumentNullException(nameof(sessionId))),
            enableWal ? new FileWalStore(storageDirectory, sessionId) : null);

    public static IContext UseSession(
        this IContext context,
        IChatStore store,
        IWalStore? walStore = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);
        return context.AddLayer(new ChatPersistenceLayer(store, walStore, context));
    }

    public static async Task<IContext> ForkSessionAsync(
        this IContext context,
        string storageDirectory,
        string sourceSessionId,
        string newSessionId,
        string? upToMessageId = null,
        bool enableWal = true,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sourceStore = new FileChatStore(storageDirectory, sourceSessionId);
        var history = await sourceStore.LoadAsync(ct).ConfigureAwait(false);
        var snapshot = history?.Snapshot(upToMessageId);
        var targetStore = new FileChatStore(storageDirectory, newSessionId);
        if (snapshot is { Count: > 0 })
            await targetStore.AppendAsync(snapshot, ct).ConfigureAwait(false);
        return context.UseSession(targetStore, enableWal ? new FileWalStore(storageDirectory, newSessionId) : null);
    }

    public static IContext RemoveSession(this IContext context)
        => context.RemoveLayer<ChatPersistenceLayer>();
}


// ============================================================================
// FILE: AgentCore.Layers/Chat/Store/ChatStore.cs (41 code lines, 46 total)
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Context.Store;
public interface IChatStore
{
    Task<IReadOnlyList<Message>?> LoadAsync(CancellationToken ct = default);
    Task AppendAsync(IReadOnlyList<Message> messages, CancellationToken ct = default);
}
public sealed class FileChatStore(string storageDirectory, string sessionId, JsonSerializerOptions? options = null) : IChatStore
{
    private readonly JsonSerializerOptions _options = options ?? StoreJson.Options;
    private readonly string _path = Path.Combine(
        !string.IsNullOrWhiteSpace(storageDirectory) ? storageDirectory : throw new ArgumentException("Storage directory cannot be null or whitespace.", nameof(storageDirectory)),
        $"{string.Join("_", (string.IsNullOrWhiteSpace(sessionId) ? throw new ArgumentException("Session ID cannot be null or whitespace.", nameof(sessionId)) : sessionId).Split(Path.GetInvalidFileNameChars()))}.jsonl");

    public async Task<IReadOnlyList<Message>?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return null;

        var lines = await File.ReadAllLinesAsync(_path, ct).ConfigureAwait(false);
        var messages = new List<Message>(lines.Length);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (JsonSerializer.Deserialize<Message>(line, _options) is { } m)
                messages.Add(m);
        }
        return messages;
    }

    public Task AppendAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0) return Task.CompletedTask;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var lines = messages.Select(m => JsonSerializer.Serialize(m, _options));
        return File.AppendAllLinesAsync(_path, lines, ct);
    }
}



// ============================================================================
// FILE: AgentCore.Layers/Chat/Store/StoreJson.cs (33 code lines, 40 total)
// ============================================================================

using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AgentCore;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Context.Store;

public static class StoreJson
{
    private static readonly Assembly[] Assemblies = [typeof(Agent).Assembly, typeof(StoreJson).Assembly];

    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { ConfigurePolymorphism }
        }
    };

    private static void ConfigurePolymorphism(JsonTypeInfo ti)
    {
        if (!ti.Type.IsInterface || (!typeof(IMessageEvent).IsAssignableFrom(ti.Type) &&
                                     !typeof(IContentEvent).IsAssignableFrom(ti.Type) &&
                                     !typeof(IContent).IsAssignableFrom(ti.Type) &&
                                     !typeof(IMetadata).IsAssignableFrom(ti.Type)))
            return;

        var poly = new JsonPolymorphismOptions { TypeDiscriminatorPropertyName = "$type" };
        var derived = Assemblies.SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && !t.IsInterface && ti.Type.IsAssignableFrom(t));

        foreach (var t in derived)
            poly.DerivedTypes.Add(new JsonDerivedType(t, t.Name));

        ti.PolymorphismOptions = poly;
    }
}


// ============================================================================
// FILE: AgentCore.Layers/Chat/Store/WalStore.cs (48 code lines, 52 total)
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore.Layers.Context.Store;
public interface IWalStore
{
    Task AppendAsync(IMessageEvent evt, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
    IAsyncEnumerable<IMessageEvent> RecoverAsync(CancellationToken ct = default);
}
public sealed class FileWalStore(string storageDirectory, string sessionId, JsonSerializerOptions? options = null) : IWalStore
{
    private readonly JsonSerializerOptions _options = options ?? StoreJson.Options;
    private readonly string _path = Path.Combine(
        !string.IsNullOrWhiteSpace(storageDirectory) ? storageDirectory : throw new ArgumentException("Storage directory cannot be null or whitespace.", nameof(storageDirectory)),
        $"{string.Join("_", (string.IsNullOrWhiteSpace(sessionId) ? throw new ArgumentException("Session ID cannot be null or whitespace.", nameof(sessionId)) : sessionId).Split(Path.GetInvalidFileNameChars()))}.wal");
    private StreamWriter? _writer;

    public async Task AppendAsync(IMessageEvent evt, CancellationToken ct = default)
    {
        if (_writer == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            _writer = new StreamWriter(_path, append: true) { AutoFlush = true };
        }
        await _writer.WriteLineAsync(JsonSerializer.Serialize(evt, _options).AsMemory(), ct).ConfigureAwait(false);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        _writer?.Dispose();
        _writer = null;
        try { File.Delete(_path); } catch { }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<IMessageEvent> RecoverAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!File.Exists(_path)) yield break;
        using var reader = new StreamReader(_path);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line) && JsonSerializer.Deserialize<IMessageEvent>(line, _options) is { } evt)
                yield return evt;
        }
    }
}


// ============================================================================
// FILE: AgentCore.Layers/LLM/InputGuardrailLayer.cs (32 code lines, 37 total)
// ============================================================================

using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using System.Runtime.CompilerServices;

namespace AgentCore.Layers.LLM;

public delegate ValueTask<IReadOnlyList<IContent>?> InputGuardrail(
    IReadOnlyList<Message> messages,
    CancellationToken ct);

public sealed class InputGuardrailLayer(InputGuardrail guardrail, ILLM? inner = null) : LLMLayer(inner)
{
    private readonly InputGuardrail _guardrail = guardrail ?? throw new ArgumentNullException(nameof(guardrail));

    public override async IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var violation = await _guardrail(messages, ct).ConfigureAwait(false);
        if (violation is { Count: > 0 })
        {
            var id = Guid.NewGuid().ToString("N");
            yield return new MessageStart(Role.Assistant, Id: id);
            foreach (var content in violation)
                yield return new MessageDelta(id, Content: content);
            yield return new MessageEnd(Id: id);
            yield break;
        }

        await foreach (var evt in base.GenerateAsync(messages, tools, responseSchema, ct).WithCancellation(ct).ConfigureAwait(false))
            yield return evt;
    }
}


// ============================================================================
// FILE: AgentCore.Layers/LLM/LLMLayerExtensions.cs (44 code lines, 51 total)
// ============================================================================

using AgentCore.LLM;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.LLM;

public static class LLMLayerExtensions
{
    public static ILLM UseRetry(
        this ILLM llm,
        int maxRetries = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double backoffMultiplier = 2.0,
        bool useJitter = true,
        Func<Exception, int, bool>? shouldRetry = null,
        Action<Exception, int, TimeSpan>? onRetry = null)
    {
        ArgumentNullException.ThrowIfNull(llm);
        return llm.AddLayer(new RetryLayer(
            llm,
            maxRetries,
            initialDelay,
            maxDelay,
            backoffMultiplier,
            useJitter,
            shouldRetry,
            onRetry));
    }

    public static ILLM RemoveRetry(this ILLM llm)
        => llm.RemoveLayer<RetryLayer>();

    public static ILLM UseToolCallDetection(this ILLM llm, bool stopAfterFirstToolCall = false)
    {
        ArgumentNullException.ThrowIfNull(llm);
        return llm.AddLayer(new ToolCallDetectionLayer(stopAfterFirstToolCall, llm));
    }

    public static ILLM RemoveToolCallDetection(this ILLM llm)
        => llm.RemoveLayer<ToolCallDetectionLayer>();

    public static ILLM UseInputGuardrail(this ILLM llm, InputGuardrail guardrail)
    {
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(guardrail);
        return llm.AddLayer(new InputGuardrailLayer(guardrail, llm));
    }

    public static ILLM RemoveInputGuardrail(this ILLM llm)
        => llm.RemoveLayer<InputGuardrailLayer>();
}


// ============================================================================
// FILE: AgentCore.Layers/LLM/RetryLayer.cs (117 code lines, 137 total)
// ============================================================================

using System.Runtime.CompilerServices;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCore.Layers.LLM;

public sealed class RetryLayer : LLMLayer
{
    private readonly int _maxRetries;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maxDelay;
    private readonly double _backoffMultiplier;
    private readonly bool _useJitter;
    private readonly Func<Exception, int, bool>? _shouldRetry;
    private readonly Action<Exception, int, TimeSpan>? _onRetry;

    public RetryLayer(
        ILLM inner,
        int maxRetries = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double backoffMultiplier = 2.0,
        bool useJitter = true,
        Func<Exception, int, bool>? shouldRetry = null,
        Action<Exception, int, TimeSpan>? onRetry = null) : base(inner)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);
        ArgumentOutOfRangeException.ThrowIfLessThan(backoffMultiplier, 1.0);
        if (double.IsNaN(backoffMultiplier) || double.IsInfinity(backoffMultiplier))
            throw new ArgumentOutOfRangeException(nameof(backoffMultiplier));

        _maxRetries = maxRetries;
        _initialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(30);

        if (_initialDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(initialDelay));
        if (_maxDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxDelay));

        _backoffMultiplier = backoffMultiplier;
        _useJitter = useJitter;
        _shouldRetry = shouldRetry;
        _onRetry = onRetry;
    }


    public override async IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var attempt = 1;
        while (true)
        {
            var yielded = false;
            IAsyncEnumerator<IMessageEvent>? enumerator = null;

            try
            {
                enumerator = Inner.GenerateAsync(messages, tools, responseSchema, ct)
                                  .GetAsyncEnumerator(ct);

                while (true)
                {
                    bool hasNext;
                    IMessageEvent? item = null;

                    try
                    {
                        hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                        if (hasNext)
                        {
                            item = enumerator.Current;
                        }
                    }
                    catch (Exception ex) when (
                        !yielded &&
                        attempt <= _maxRetries &&
                        (_shouldRetry?.Invoke(ex, attempt) ?? IsTransient(ex)))
                    {
                        var delay = GetDelay(attempt);
                        _onRetry?.Invoke(ex, attempt, delay);
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                        attempt++;
                        break;
                    }

                    if (!hasNext)
                    {
                        yield break;
                    }

                    yielded = true;
                    yield return item!;
                }
            }
            finally
            {
                if (enumerator != null)
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    public static bool IsTransient(Exception exception)
    {
        if (exception is TimeoutException or IOException)
            return true;

        if (exception is HttpRequestException http)
        {
            var status = (int?)http.StatusCode;
            return status is null or 408 or 429 or >= 500;
        }

        return false;
    }

    private TimeSpan GetDelay(int attempt)
    {
        var exponential = _initialDelay.TotalMilliseconds *
                          Math.Pow(_backoffMultiplier, attempt - 1);

        var delay = Math.Min(exponential, _maxDelay.TotalMilliseconds);

        if (_useJitter)
        {
            delay *= 0.5 + Random.Shared.NextDouble() * 0.5;
        }

        return TimeSpan.FromMilliseconds(delay);
    }
}


// ============================================================================
// FILE: AgentCore.Layers/LLM/ToolCallDetectionLayer.cs (174 code lines, 200 total)
// ============================================================================

using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using AgentCore.Tool.Tools;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AgentCore.Layers.LLM;
 
public sealed class ToolCallDetectionLayer(bool stopAfterFirstToolCall = false, ILLM? inner = null) : LLMLayer(inner)
{ 
    private static readonly Regex TagPattern = new(
        @"[\[\(<](?<tag>[^\]\)>]*?tool[^\]\)>]*?)[\]\)>]\s*(?<content>[\s\S]*?)\s*[\[\(<]/\k<tag>[\]\)>]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex MarkdownPattern = new(
        @"```json\s*(?<json>\{[\s\S]*?\})\s*```",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex XmlFuncPattern = new(
        @"(?i)<function\s*(?:=|\bname\s*=)\s*""?(?<name>[a-zA-Z0-9_\-]+)""?\s*>", RegexOptions.Compiled);

    private static readonly Regex XmlParamPattern = new(
        @"<parameter\s*=\s*""?(?<name>[a-zA-Z0-9_\-]+)""?\s*>(?<val>[\s\S]*?)</parameter>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public override IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        CancellationToken ct = default)
    {
        var toolNames = tools?.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        return toolNames.Count == 0
            ? Inner.GenerateAsync(messages, tools, responseSchema, ct)
            : ProcessStreamAsync(Inner.GenerateAsync(messages, tools, responseSchema, ct), toolNames, ct);
    }

    private async IAsyncEnumerable<IMessageEvent> ProcessStreamAsync(
        IAsyncEnumerable<IMessageEvent> innerStream,
        HashSet<string> toolNames,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var buffers = new Dictionary<int, StringBuilder>();
        int eventIndex = 0;

        await foreach (var evt in innerStream.WithCancellation(ct).ConfigureAwait(false))
        {
            switch (evt)
            {
                case MessageDelta { Content: TextStart s }:
                    buffers[s.Index] = new();
                    eventIndex = Math.Max(eventIndex, s.Index + 1);
                    break;

                case MessageDelta { Content: TextDelta d }:
                    if (!buffers.TryGetValue(d.Index, out var tb)) buffers[d.Index] = tb = new();
                    tb.Append(d.Text);
                    break;

                case MessageDelta { Content: TextEnd te } when buffers.Remove(te.Index, out var sb):
                    foreach (var parsedEvt in EmitParsedText(sb.ToString()))
                    {
                        yield return parsedEvt;
                        if (parsedEvt is MessageDelta { Content: ToolCallEnd } && stopAfterFirstToolCall) yield break;
                    }
                    break;

                case MessageEnd:
                    foreach (var (_, sb) in buffers)
                    {
                        foreach (var parsedEvt in EmitParsedText(sb.ToString()))
                        {
                            yield return parsedEvt;
                            if (parsedEvt is MessageDelta { Content: ToolCallEnd } && stopAfterFirstToolCall) yield break;
                        }
                    }
                    buffers.Clear();
                    yield return evt;
                    break;

                default:
                    yield return evt;
                    if (evt is MessageDelta { Content: ToolCallEnd } && stopAfterFirstToolCall) yield break;
                    break;
            }
        }

        IEnumerable<IMessageEvent> EmitParsedText(string text)
        {
            int lastIndex = 0;
            while (lastIndex < text.Length)
            {
                var match = FindToolCall(text, lastIndex, toolNames);
                if (match == null)
                {
                    var remaining = text[lastIndex..];
                    if (!string.IsNullOrEmpty(remaining))
                    {
                        int idx = eventIndex++;
                        yield return new MessageDelta(Content: new TextStart(idx));
                        yield return new MessageDelta(Content: new TextDelta(idx, remaining));
                        yield return new MessageDelta(Content: new TextEnd(idx));
                    }
                    break;
                }

                if (match.Index > lastIndex)
                {
                    var leading = text[lastIndex..match.Index];
                    if (!string.IsNullOrEmpty(leading))
                    {
                        int idx = eventIndex++;
                        yield return new MessageDelta(Content: new TextStart(idx));
                        yield return new MessageDelta(Content: new TextDelta(idx, leading));
                        yield return new MessageDelta(Content: new TextEnd(idx));
                    }
                }

                int tcIdx = eventIndex++;
                yield return new MessageDelta(Content: new ToolCallStart(tcIdx, match.Call.Id, match.Call.Name));
                yield return new MessageDelta(Content: new ToolCallDelta(tcIdx, string.IsNullOrWhiteSpace(match.Call.Arguments) ? "{}" : match.Call.Arguments));
                yield return new MessageDelta(Content: new ToolCallEnd(tcIdx));

                lastIndex = match.Index + match.Length;
            }
        }
    }

    private record ToolMatch(ToolCall Call, int Index, int Length);

    private static ToolMatch? FindToolCall(string text, int startIndex, HashSet<string> toolNames)
    {
        // 1. Tag wrapper (<tool_call>...</tool_call>)
        var tag = TagPattern.Match(text, startIndex);
        if (tag.Success && ExtractTag(tag.Groups["content"].Value, toolNames) is { } tc1)
            return new(tc1, tag.Index, tag.Length);

        // 2. Markdown codeblock (```json { ... } ```)
        var md = MarkdownPattern.Match(text, startIndex);
        if (md.Success && ExtractJson(md.Groups["json"].Value, toolNames) is { } tc2)
            return new(tc2, md.Index, md.Length);

        // 3. Raw JSON ({ "name": "...", "arguments": { ... } })
        return ExtractRawJson(text, startIndex, toolNames);
    }

    private static ToolMatch? ExtractRawJson(string text, int startIndex, HashSet<string> toolNames)
    {
        int first = text.IndexOf('{', startIndex);
        if (first < 0) return null;

        int depth = 0;
        bool inStr = false, esc = false;
        for (int i = first; i < text.Length; i++)
        {
            char c = text[i];
            if (esc) { esc = false; continue; }
            if (inStr && c == '\\') { esc = true; continue; }
            if (c == '"') { inStr = !inStr; continue; }
            if (!inStr)
            {
                if (c == '{') depth++;
                else if (c == '}' && --depth == 0)
                    return ExtractJson(text[first..(i + 1)], toolNames) is { } tc ? new(tc, first, i - first + 1) : null;
            }
        }
        return null;
    }

    private static ToolCall? ExtractTag(string content, HashSet<string> names) =>
        ExtractJson(content, names) ?? ExtractXml(content, names);

    private static ToolCall? ExtractJson(string jsonStr, HashSet<string> names)
    {
        try
        {
            if (JsonNode.Parse(jsonStr) is JsonObject obj && (obj["name"] ?? obj["tool"])?.ToString() is { } name && names.Contains(name))
                return new ToolCall(Guid.NewGuid().ToString("N"), name, (obj["arguments"] ?? obj["parameters"])?.ToJsonString() ?? "{}");
        }
        catch { }
        return null;
    }

    private static ToolCall? ExtractXml(string content, HashSet<string> names)
    {
        if (XmlFuncPattern.Match(content) is not { Success: true } m || !names.Contains(m.Groups["name"].Value)) return null;

        var args = new JsonObject();
        foreach (Match p in XmlParamPattern.Matches(content))
        {
            var val = p.Groups["val"].Value.Trim();
            try { args[p.Groups["name"].Value] = JsonNode.Parse(val)?.DeepClone(); }
            catch { args[p.Groups["name"].Value] = val; }
        }
        return new ToolCall(Guid.NewGuid().ToString("N"), m.Groups["name"].Value, args.ToJsonString());
    }
}


// ============================================================================
// FILE: AgentCore.Layers/Tool/ToolApprovalLayer.cs (41 code lines, 49 total)
// ============================================================================

using AgentCore.LLM;
using AgentCore.LLM.Chat;
using System.Runtime.CompilerServices;

namespace AgentCore.Tool;

public delegate Task<IReadOnlyList<IContent>?> ToolApprover(ToolCall call, CancellationToken ct);

public sealed class ToolApprovalLayer(ToolApprover approver, IToolbox? inner = null) : ToolboxLayer(inner)
{
    private readonly ToolApprover _approver = approver ?? throw new ArgumentNullException(nameof(approver));

    public ToolApprovalLayer(Func<ToolCall, CancellationToken, Task<IContent?>> evaluator, IToolbox? inner = null)
        : this(async (call, ct) => (await evaluator(call, ct).ConfigureAwait(false)) is { } c ? [c] : null, inner) { }

    public ToolApprovalLayer(Func<ToolCall, CancellationToken, Task<bool>> prompt, IToolbox? inner = null)
        : this(async (call, ct) => await prompt(call, ct).ConfigureAwait(false) ? null : [new Text($"Execution of tool '{call.Name}' was rejected by the user.")], inner) { }

    public override async IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var allowedCalls = new List<ToolCall>();

        foreach (var call in calls)
        {
            var denial = await _approver(call, ct).ConfigureAwait(false);
            if (denial is { Count: > 0 })
            {
                yield return new MessageStart(Role.Tool, Id: call.Id);
                var contents = denial.Select(item => item is IToolResultContent trc ? trc : new Text(item.ToString() ?? string.Empty)).ToList();
                yield return new MessageDelta(call.Id, Content: new ToolResult(call.Id, contents, isError: true));
                yield return new MessageEnd(Id: call.Id);
            }
            else
            {
                allowedCalls.Add(call);
            }
        }

        if (allowedCalls.Count > 0)
        {
            await foreach (var evt in base.ExecuteAsync(allowedCalls, ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
    }
}


// ============================================================================
// FILE: AgentCore.Layers/Tool/ToolDiscoveryLayer.cs (97 code lines, 112 total)
// ============================================================================

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCore.Layers.Tools;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class Discoverable(string? domain = null) : Attribute, IMetadata
{
    public string? Domain { get; } = domain;
}

public sealed class ToolDiscoveryTool : ITool
{
    private readonly ConcurrentDictionary<string, byte> _active = new(StringComparer.OrdinalIgnoreCase);

    public Func<IReadOnlyList<ToolDefinition>>? CatalogProvider { get; set; }

    public ToolDefinition Definition { get; } = new(
        "search_tools",
        "Searches available tools in catalog by keyword or domain to activate them into context.",
        new JsonSchemaBuilder()
            .Type<object>()
            .AddProperty("query", new JsonSchemaBuilder().Type<string>().Description("Search keyword or domain").Build(), required: true)
            .Build());

    public bool IsActive(ToolDefinition tool) =>
        tool.Metadata.Get<Discoverable>() == null || _active.ContainsKey(tool.Name);

    public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        JsonObject arguments,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        var query = arguments["query"]?.ToString();
        if (string.IsNullOrWhiteSpace(query))
        {
            yield return new Text("Please provide a search query.");
            yield break;
        }

        var catalog = CatalogProvider?.Invoke() ?? [];
        var matches = catalog
            .Where(t => !IsActive(t) && (
                t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (t.Metadata.Get<Discoverable>()?.Domain is { Length: > 0 } d &&
                 d.Contains(query, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        if (matches.Count == 0)
        {
            yield return new Text($"No tools found matching '{query}'.");
            yield break;
        }

        foreach (var m in matches) _active.TryAdd(m.Name, 0);

        yield return new Text($"Activated {matches.Count} tool(s):\n" +
            string.Join("\n", matches.Select(m => $"- {m.Name}: {m.Description}")));
    }
}

public sealed class ToolDiscoveryLayer(ToolDiscoveryTool tool, IToolbox? inner = null) : ToolboxLayer(inner)
{
    public ToolDiscoveryTool Tool { get; } = tool ?? throw new ArgumentNullException(nameof(tool));

    public override async ValueTask<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken ct = default)
    {
        var innerTools = Inner != null ? await Inner.GetToolsAsync(ct).ConfigureAwait(false) : [];
        Tool.CatalogProvider = () => innerTools.Select(t => t.Definition).ToArray();
        return [.. innerTools.Where(t => Tool.IsActive(t.Definition)), Tool];
    }

    public override async IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var innerCalls = new List<ToolCall>();
        foreach (var call in calls)
        {
            if (string.Equals(call.Name, Tool.Definition.Name, StringComparison.OrdinalIgnoreCase))
            {
                var (args, _) = call.ParseArguments();
                yield return new MessageStart(Role.Tool, Id: call.Id);
                var contents = new List<IToolResultContent>();
                await foreach (var evt in Tool.InvokeStreamingAsync(args ?? [], ct).ConfigureAwait(false))
                {
                    if (evt is IToolResultContent trc) contents.Add(trc);
                    else if (evt is Text t) contents.Add(t);
                }
                yield return new MessageDelta(call.Id, Content: new ToolResult(call.Id, contents));
                yield return new MessageEnd(Id: call.Id);
            }
            else
            {
                innerCalls.Add(call);
            }
        }

        if (innerCalls.Count > 0)
        {
            await foreach (var evt in base.ExecuteAsync(innerCalls, ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
    }
}


// ============================================================================
// FILE: AgentCore.Layers/Tool/ToolingLayerExtensions.cs (27 code lines, 33 total)
// ============================================================================

using AgentCore.LLM.Chat;
using AgentCore.Tool;

namespace AgentCore.Layers.Tools;

public static class ToolingLayerExtensions
{
    public static IToolbox UseApproval(this IToolbox tooling, ToolApprover approver)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        ArgumentNullException.ThrowIfNull(approver);
        return tooling.AddLayer(new ToolApprovalLayer(approver, tooling));
    }

    public static IToolbox UseApproval(this IToolbox tooling, Func<ToolCall, CancellationToken, Task<bool>> prompt)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        ArgumentNullException.ThrowIfNull(prompt);
        return tooling.AddLayer(new ToolApprovalLayer(prompt, tooling));
    }

    public static IToolbox RemoveApproval(this IToolbox tooling)
        => tooling.RemoveLayer<ToolApprovalLayer>();

    public static IToolbox UseToolDiscovery(this IToolbox tooling, ToolDiscoveryTool? tool = null)
    {
        ArgumentNullException.ThrowIfNull(tooling);
        return tooling.AddLayer(new ToolDiscoveryLayer(tool ?? new ToolDiscoveryTool(), tooling));
    }

    public static IToolbox RemoveToolDiscovery(this IToolbox tooling)
        => tooling.RemoveLayer<ToolDiscoveryLayer>();
}


// ============================================================================
// FILE: AgentCore.LLM.Tornado/TornadoAdapterExtensions.cs (100 code lines, 115 total)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AgentCore.LLM.Chat;
using AgentCore.Tool;
using LlmTornado.Chat;
using LlmTornado.ChatFunctions;
using LlmTornado.Code;
using LlmTornado.Common;
using LlmTornado.Images;
using ToolCall = AgentCore.LLM.Chat.ToolCall;

namespace AgentCore.LLM.Tornado;

public static class TornadoAdapterExtensions
{
    public static ChatMessageRoles ToTornadoRole(this Role role) => role switch
    {
        Role.System => ChatMessageRoles.System,
        Role.User => ChatMessageRoles.User,
        Role.Assistant => ChatMessageRoles.Assistant,
        Role.Tool => ChatMessageRoles.Tool,
        _ => throw new ArgumentOutOfRangeException(nameof(role), $"Unsupported role: {role}")
    };

    public static ChatMessage ToTornadoMessage(this Message message)
    {
        var role = message.Role.ToTornadoRole();
        var tornadoMsg = new ChatMessage(role);

        if (message.Role == Role.Tool)
            tornadoMsg.ToolCallId = message.Contents.OfType<ToolResult>().FirstOrDefault()?.ToolCallId ?? message.Id;

        var textParts = new List<string>();
        List<ChatMessagePart>? parts = null;
        List<LlmTornado.ChatFunctions.ToolCall>? toolCalls = null;

        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case Text text: textParts.Add(text.Value); break;
                case ToolResult tr: textParts.AddRange(tr.Contents.OfType<Text>().Select(t => t.Value)); break;

                case Reasoning reasoning:
                    tornadoMsg.Reasoning = reasoning.Thought;
                    break;

                case Image img:
                    parts ??= [];
                    if (img.Uri != null)
                        parts.Add(new ChatMessagePart(img.Uri));
                    else if (img.Data != null)
                        parts.Add(new ChatMessagePart(Convert.ToBase64String(img.Data.Value.ToArray()), ImageDetail.Auto, img.MediaType));
                    break;

                case Audio audio:
                    parts ??= [];
                    var audioFormat = audio.MediaType.Contains("mp3", StringComparison.OrdinalIgnoreCase) ? ChatAudioFormats.Mp3 : ChatAudioFormats.Wav;
                    if (audio.Data != null)
                        parts.Add(new ChatMessagePart(audio.Data.Value.ToArray(), audioFormat));
                    else if (audio.Uri != null)
                        parts.Add(new ChatMessagePart(new ChatMessagePartFileLinkData(audio.Uri.AbsoluteUri, audio.MediaType)));
                    break;

                case Video video:
                    parts ??= [];
                    if (video.Uri != null)
                        parts.Add(new ChatMessagePart(new ChatMessagePartFileLinkData(video.Uri.AbsoluteUri, video.MediaType)));
                    break;

                case ToolCall tc:
                    toolCalls ??= [];
                    var argsStr = string.IsNullOrWhiteSpace(tc.Arguments) ? "{}" : tc.Arguments;
                    toolCalls.Add(new LlmTornado.ChatFunctions.ToolCall
                    {
                        Id = tc.Id,
                        FunctionCall = new FunctionCall
                        {
                            Name = tc.Name,
                            Arguments = argsStr
                        }
                    });
                    break;
            }
        }

        if (parts is { Count: > 0 })
        {
            if (textParts.Count > 0)
                parts.Insert(0, new ChatMessagePart(string.Join("\n", textParts)));
            tornadoMsg.Parts = parts;
        }
        else if (textParts.Count > 0)
        {
            tornadoMsg.Content = string.Join("\n", textParts);
        }

        if (toolCalls is { Count: > 0 })
        {
            tornadoMsg.ToolCalls = toolCalls;
        }

        return tornadoMsg;
    }

    public static LlmTornado.Common.Tool ToTornadoTool(this ToolDefinition tool)
    {
        var jsonElem = tool.ParametersSchema.ToJsonElement();
        var fn = new ToolFunction(tool.Name, tool.Description, jsonElem);
        return new LlmTornado.Common.Tool(fn);
    }
}
public sealed record TornadoUsage(ChatUsage Raw) : IMetadata;


// ============================================================================
// FILE: AgentCore.LLM.Tornado/TornadoLLM.cs (169 code lines, 192 total)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;
using LlmTornado;
using LlmTornado.Chat;
using LlmTornado.Chat.Models;
using LlmTornado.ChatFunctions;
using LlmTornado.Code;
using LlmTornado.Responses.Events;

namespace AgentCore.LLM.Tornado;

/// <summary>
/// Adapts LLMTornado to the AgentCore ILLM event-streaming interface via direct SSE streaming.
/// Preserves real-time token-by-token streaming for reasoning, text, and tool-call deltas.
/// </summary>
public sealed class TornadoLLM(TornadoApi api, ChatModel model) : ILLM
{
    public TornadoLLM(string apiKey, string model, string? baseUrl = null, LLmProviders provider = LLmProviders.Custom)
        : this(CreateApi(apiKey, baseUrl, provider), new ChatModel(model, provider))
    {
    }

    private static TornadoApi CreateApi(string apiKey, string? baseUrl, LLmProviders provider)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return new TornadoApi(provider, apiKey);
        var cleanUrl = baseUrl.TrimEnd('/');
        if (cleanUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) cleanUrl = cleanUrl[..^3];
        return new TornadoApi(new Uri(cleanUrl + "/"), apiKey, provider);
    }
    public async IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var conv = api.Chat.CreateConversation();
        conv.Model = model;
        
        // Populate messages cleanly
        conv.AddMessage(messages.Select(m => m.ToTornadoMessage()));

        conv.RequestParameters.Tools = tools?.Select(t => t.ToTornadoTool()).ToList();
        conv.RequestParameters.ResponseFormat = responseSchema != null ? ChatRequestResponseFormats.StructuredJson("response", responseSchema.ToJsonElement()) : null;
        conv.RequestParameters.StreamOptions = ChatStreamOptions.KnownOptionsIncludeUsage;
        conv.RequestParameters.MaxTokens = 4096;

        var channel = Channel.CreateUnbounded<IMessageEvent>();

        _ = Task.Run(async () =>
        {
            try
            {
                int nextId = 0;
                int? textBlockId = null;
                int? reasoningBlockId = null;
                bool started = false;

                // Tool mapping for deltas
                var toolBlocks = new Dictionary<int, int>(); // LlmTornado Index -> AgentCore blockId

                async ValueTask EnsureStarted()
                {
                    if (!started)
                    {
                        started = true;
                        await channel.Writer.WriteAsync(new MessageStart(Role.Assistant), ct);
                    }
                }

                ValueTask WriteContent(IContentEvent content) =>
                    channel.Writer.WriteAsync(new MessageDelta(Content: content), ct);

                async ValueTask CloseActiveBlocks(bool closeReasoning = true, bool closeText = true)
                {
                    if (closeText && textBlockId != null)
                    {
                        await WriteContent(new TextEnd(textBlockId.Value));
                        textBlockId = null;
                    }
                    if (closeReasoning && reasoningBlockId != null)
                    {
                        await WriteContent(new ReasoningEnd(reasoningBlockId.Value));
                        reasoningBlockId = null;
                    }
                }

                var handler = new ChatStreamEventHandler
                {
                    MessageTypeResolvedHandler = async (role) => await EnsureStarted(),
                    ReasoningTokenHandler = async (data) =>
                    {
                        await EnsureStarted();
                        await CloseActiveBlocks(closeReasoning: false);
                        
                        if (reasoningBlockId == null)
                        {
                            reasoningBlockId = nextId++;
                            await WriteContent(new ReasoningStart(reasoningBlockId.Value));
                        }
                        if (!string.IsNullOrEmpty(data.Content))
                        {
                            await WriteContent(new ReasoningDelta(reasoningBlockId.Value, data.Content));
                        }
                    },
                    MessageTokenExHandler = async (tokenData) =>
                    {
                        await EnsureStarted();
                        await CloseActiveBlocks(closeText: false);
                        
                        if (textBlockId == null)
                        {
                            textBlockId = nextId++;
                            await WriteContent(new TextStart(textBlockId.Value));
                        }
                        if (!string.IsNullOrEmpty(tokenData.Content))
                        {
                            await WriteContent(new TextDelta(textBlockId.Value, tokenData.Content));
                        }
                    },
                    FunctionCallDeltaHandler = async (delta) =>
                    {
                        await EnsureStarted();
                        await CloseActiveBlocks();

                        int toolIndex = delta.Index ?? 0;
                        if (!toolBlocks.TryGetValue(toolIndex, out int blockId))
                        {
                            blockId = nextId++;
                            toolBlocks[toolIndex] = blockId;
                            
                            var callId = delta.CallId ?? $"call_{blockId}";
                            await WriteContent(new ToolCallStart(blockId, callId, delta.Name ?? ""));
                        }

                        if (!string.IsNullOrEmpty(delta.ArgumentsDelta))
                        {
                            await WriteContent(new ToolCallDelta(blockId, delta.ArgumentsDelta));
                        }

                        if (delta.IsComplete)
                        {
                            await WriteContent(new ToolCallEnd(blockId));
                            toolBlocks.Remove(toolIndex);
                        }
                    },
                    OnFinished = async (data) =>
                    {
                        await EnsureStarted();
                        await CloseActiveBlocks();

                        foreach (var kvp in toolBlocks.OrderBy(x => x.Value))
                        {
                            await WriteContent(new ToolCallEnd(kvp.Value));
                        }
                        toolBlocks.Clear();

                        if (data.Usage != null)
                        { 
                            await channel.Writer.WriteAsync(new MessageDelta(Metadata: new TokenUsage(data.Usage.PromptTokens, data.Usage.CompletionTokens, data.Usage.TotalTokens)), ct);
                            await channel.Writer.WriteAsync(new MessageDelta(Metadata: new TornadoUsage(data.Usage)), ct);
                        }

                        await channel.Writer.WriteAsync(new MessageEnd(), ct);
                    }
                };

                await conv.StreamResponseRich(handler, ct);
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, ct);

        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
        {
            yield return evt;
        }
    }
}


// ============================================================================
// FILE: AgentCore.MCP/McpTool.cs (44 code lines, 50 total)
// ============================================================================

using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentCore.Tool;
using ProtocolTool = ModelContextProtocol.Protocol.Tool;

namespace AgentCore.MCP;

public sealed class McpTool(McpClient client, ProtocolTool tool) : ITool
{
    private readonly McpClient _client = client ?? throw new ArgumentNullException(nameof(client));  
    private static JsonSchema ParseSchema(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
            ? new JsonSchema((JsonNode.Parse(element.GetRawText()) as JsonObject) ?? new JsonObject())
            : new JsonSchema(new JsonObject());
    public ToolDefinition Definition { get; } = new(tool.Name, tool.Description ?? tool.Name, ParseSchema(tool.InputSchema));

    public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        JsonObject arguments,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var dict = arguments?.Count > 0
            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments.ToJsonString())
            : null;

        var result = await _client.CallToolAsync(Definition.Name, dict, cancellationToken: ct).ConfigureAwait(false);

        if (result.IsError == true)
        {
            var msg = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text));
            throw new InvalidOperationException($"MCP tool '{Definition.Name}' failed: {msg}");
        }

        foreach (var b in result.Content)
        {
            IContent content = b switch
            {
                TextContentBlock tb => new Text(tb.Text),
                ImageContentBlock ib => new Image(Data: ib.Data, MediaType: ib.MimeType),
                _ => new Text(b.ToString() ?? string.Empty)
            };
            yield return content;
        }
    }
}


// ============================================================================
// FILE: AgentCore.MCP/McpToolBuilderExtensions.cs (15 code lines, 18 total)
// ============================================================================

using AgentCore;
using AgentCore.Tool;
using ModelContextProtocol.Client;

namespace AgentCore.MCP;

public static class McpToolExtensions
{ 
    public static async Task<IToolbox> AddMcpToolsAsync(this IToolbox toolbox, McpClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(client);

        var tools = await client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
        var mcpTools = tools.Select(t => (ITool)new McpTool(client, t.ProtocolTool));
        return toolbox.AddTool(mcpTools);
    }
} 

// ============================================================================
// FILE: AgentCore/Agent.cs (74 code lines, 86 total)
// ============================================================================

using System.Runtime.CompilerServices;
using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tool;

namespace AgentCore;

public interface IAgent
{
    IReadOnlyList<IContent> Instructions { get; }
    IContext Context { get; }
    ILLM LLM { get; }
    IToolbox Toolbox { get; }

    IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        IReadOnlyList<IContent> input,
        CancellationToken ct = default);
}

public sealed class Agent(
    ILLM llm,
    IToolbox? toolbox = null,
    IContext? context = null,
    IReadOnlyList<IContent>? instructions = null,
    int maxIterations = 20) : IAgent
{
    public ILLM LLM { get; } = llm?.GetType() == typeof(LLMLayer) ? (LLMLayer)llm : new LLMLayer(llm ?? throw new ArgumentNullException(nameof(llm)));
    public IToolbox Toolbox { get; } = toolbox?.GetType() == typeof(ToolboxLayer) ? (ToolboxLayer)toolbox : new ToolboxLayer(toolbox ?? new Toolbox());
    public IContext Context { get; } = context?.GetType() == typeof(ContextLayer) ? (ContextLayer)context : new ContextLayer(context ?? new ChatContext());
    public IReadOnlyList<IContent> Instructions { get; } = instructions ?? [new Text("You are a helpful AI assistant.")];
    public int MaxIterations { get; } = maxIterations;

    public Agent(
        ILLM llm,
        Func<IToolbox, IToolbox>? toolbox = null,
        Func<IContext, IContext>? context = null,
        IReadOnlyList<IContent>? instructions = null,
        int maxIterations = 20)
        : this(
            llm,
            toolbox != null ? toolbox(new Toolbox()) : null,
            context != null ? context(new ChatContext()) : null,
            instructions,
            maxIterations)
    {
    }

    public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        IReadOnlyList<IContent> input,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await Context.WriteAsync(new Message(Role.User, input), ct).ConfigureAwait(false);
        var messages = await Context.ReadAsync(ct).ConfigureAwait(false);

        int iterations = 0;
        List<ToolCall>? toolCalls;
        do
        {
            ct.ThrowIfCancellationRequested();
            if (++iterations > MaxIterations)
                throw new InvalidOperationException($"Execution exceeded maximum limit of {MaxIterations} iterations.");

            var toolDefs = await Toolbox.GetDefinitionsAsync(ct).ConfigureAwait(false);
            List<Message> prompt = [new Message(Role.System, Instructions), .. messages];

            toolCalls = null;
            await foreach (var evt in Context.WriteAsync(LLM.GenerateAsync(prompt, toolDefs, ct: ct), ct))
            if (evt is MessageDelta { Content: { } c })
            {
                if (c is ToolCall tc) (toolCalls ??= []).Add(tc);
                yield return c;
            }

            if (toolCalls is not null)
            {
                await foreach (var evt in Context.WriteAsync(Toolbox.ExecuteAsync(toolCalls, ct), ct))
                    if (evt is MessageDelta { Content: { } c }) yield return c;

                messages = await Context.ReadAsync(ct).ConfigureAwait(false);
            }
        } while (toolCalls is not null);
    }
}


// ============================================================================
// FILE: AgentCore/AgentExtensions.cs (39 code lines, 45 total)
// ============================================================================

using AgentCore.Context;
using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.Tool;

namespace AgentCore;

public static class AgentExtensions
{
    public static Agent With(
        this IAgent agent,
        ILLM? llm = null,
        IToolbox? toolbox = null,
        IContext? context = null,
        IReadOnlyList<IContent>? instructions = null,
        int? maxIterations = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return new(
            llm ?? agent.LLM,
            toolbox ?? agent.Toolbox,
            context ?? agent.Context,
            instructions ?? agent.Instructions,
            maxIterations ?? (agent as Agent)?.MaxIterations ?? 20);
    }

    public static async Task<string?> GetFinalResponseAsync(
        this IAsyncEnumerable<IContentEvent> stream,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        string? text = null;

        await foreach (var evt in stream.WithCancellation(ct).ConfigureAwait(false))
            if (evt is Text t) text = t.Value;

        return text;
    }
}

public interface ILayer<T>
{
    T Inner { get; }
    void Attach(T inner);
}


// ============================================================================
// FILE: AgentCore/Context/ChatContext.cs (116 code lines, 132 total)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.Context.Primitives;
using AgentCore.LLM.Chat;
using Microsoft.Extensions.Logging;

namespace AgentCore.Context;

public interface IContext
{
    Task<IReadOnlyList<Message>> ReadAsync(CancellationToken ct = default);
    IAsyncEnumerable<IMessageEvent> WriteAsync(IAsyncEnumerable<IMessageEvent> events, CancellationToken ct = default);
}

public sealed class ChatContext(
    int contextWindow = 50000, int? reserveTokens = null, int? maxSingleMessageTokens = null,
    ICompactor? compactor = null, ITokenizer? counter = null, ITruncator? truncator = null,
    IAssembler? assembler = null, INormalizer? normalizer = null, ILogger<ChatContext>? logger = null,
    IEnumerable<Message>? messages = null) : IContext
{
    private Message[] _chat = messages?.ToArray() ?? [];
    private readonly Dictionary<string, IAssembler> _open = new(StringComparer.Ordinal);
    private readonly IAssembler _assembler = assembler ?? new Assembler();
    private readonly ITokenizer _counter = counter ?? new Tokenizer();
    private readonly ITruncator _truncator = truncator ?? new Truncator(counter ?? new Tokenizer());
    private readonly INormalizer _normalizer = normalizer ?? new ChatNormalizer();
    private readonly int _limit = Math.Max(1, contextWindow - (reserveTokens ?? Math.Min(4_000, contextWindow / 10)));
    private readonly int _maxTokens = maxSingleMessageTokens ?? Math.Max(125, Math.Min(10_000, contextWindow / 5));
    private readonly object _lock = new();
    private string? _activeId;
    private int _tokens = messages != null ? messages.Sum(m => (int)((1 + m.Contents.Sum((counter ?? new Tokenizer()).Estimate)) * 1.15)) : 0;

    public async IAsyncEnumerable<IMessageEvent> WriteAsync(
        IAsyncEnumerable<IMessageEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        try
        {
            await foreach (var evt in events.WithCancellation(ct).ConfigureAwait(false))
            {
                IMessageEvent? completed;
                lock (_lock) completed = AppendLocked(evt);

                yield return evt;
                if (completed is not null) yield return completed;
            }
        }
        finally
        {
            lock (_lock)
            {
                foreach (var (id, asm) in _open.ToList())
                    if (asm.ToMessage(new MessageEnd(id)) is { Contents.Count: > 0 } msg)
                        Commit(msg);
                _open.Clear();
                _activeId = null;
            }
        }
    }

    private IMessageEvent? AppendLocked(IMessageEvent evt)
    {
        if (evt is Message m)
        {
            Commit(m, m.Metadata.Get<TokenUsage>()?.TotalTokens);
            return null;
        }

        var id = evt.Id;

        if (evt is MessageStart ms)
        {
            id ??= Guid.NewGuid().ToString("N");
            if (ms.Role != Role.Tool) _activeId = id;
            _open[id] = _assembler.Create(ms);
            return null;
        }

        id ??= _activeId ??= Guid.NewGuid().ToString("N");
        if (!_open.TryGetValue(id, out var asm))
            asm = _open[id] = _assembler.Create();

        if (evt is MessageEnd me)
        {
            _open.Remove(id);
            if (id == _activeId) _activeId = null;
            var msg = asm.ToMessage(me);
            Commit(msg, msg.Metadata.Get<TokenUsage>()?.TotalTokens);
            return msg;
        }

        return evt is MessageDelta md && asm.Push(md) is { } c ? new MessageDelta(id, Content: c) : null;
    }

    public async Task<IReadOnlyList<Message>> ReadAsync(CancellationToken ct = default)
    {
        Message[] snapshot;
        lock (_lock) snapshot = _chat;

        if (_tokens > _limit && compactor != null)
        {
            logger?.LogInformation("Context overflow ({Tokens}/{Limit}). Compacting via {Compactor}...", _tokens, _limit, compactor.GetType().Name);
            var compacted = await compactor.CompactAsync(_normalizer.Normalize(snapshot), _limit, ct).ConfigureAwait(false);
            lock (_lock)
            {
                _chat = [.. compacted, .. _chat.Skip(snapshot.Length)];
                _tokens = _chat.Sum(Estimate);
                snapshot = _chat;
            }
            logger?.LogInformation("Compacted: {Count} messages ({Tokens} tokens).", snapshot.Length, _tokens);
        }

        var normalized = _normalizer.Normalize(snapshot);
        logger?.LogDebug("Context staged: {Count} messages ({Tokens}/{Limit} tokens).", normalized.Count, _tokens, _limit);
        return normalized;
    }

    private Message Commit(Message m, int? tokens = null)
    {
        var truncated = m.Role == Role.System ? m : new Message(m.Role, m.Contents.Select(c => _truncator.Truncate(c, _maxTokens)).ToList(), m.Id, m.Metadata);
        _chat = [.. _chat, truncated];
        _tokens = tokens ?? (_tokens + Estimate(truncated));
        return truncated;
    }

    private int Estimate(Message m) => (int)((1 + m.Contents.Sum(_counter.Estimate)) * 1.15);
}


// ============================================================================
// FILE: AgentCore/Context/ContextExtensions.cs (57 code lines, 64 total)
// ============================================================================

using AgentCore.LLM.Chat;

namespace AgentCore.Context;

public static class ContextExtensions
{
    public static async Task WriteAsync(this IContext context, IMessageEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(evt);
        await foreach (var _ in context.WriteAsync(Stream(evt), ct).ConfigureAwait(false)) { }
        static async IAsyncEnumerable<IMessageEvent> Stream(IMessageEvent e) { yield return e; }
    }

    public static IContext Attach(this IContext context, IContext inner)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(inner);
        if (context is ILayer<IContext> layer) layer.Attach(inner);
        return context;
    }

    public static IContext AddLayer(this IContext context, IContext layer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(layer);
        if (context is ILayer<IContext> head && layer is ILayer<IContext> next)
        {
            next.Attach(head.Inner);
            head.Attach(layer);
            return context;
        }
        if (layer is ILayer<IContext> l) l.Attach(context);
        return layer;
    }

    public static IContext RemoveLayer<T>(this IContext context) where T : class
    {
        if (context is T && context is ILayer<IContext> self) return self.Inner.RemoveLayer<T>();
        if (context is ILayer<IContext> head)
        {
            var newInner = head.Inner.RemoveLayer<T>();
            if (!ReferenceEquals(newInner, head.Inner)) head.Attach(newInner);
        }
        return context;
    }

    public static TL? FindLayer<TL>(this IContext root) where TL : class
    {
        for (var c = root; c != null; c = (c as ILayer<IContext>)?.Inner)
            if (c is TL match) return match;
        return null;
    }

    public static IReadOnlyList<Message> Snapshot(this IReadOnlyList<Message> messages, string? upToMessageId = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (upToMessageId == null) return messages;
        for (int i = 0; i < messages.Count; i++)
            if (string.Equals(messages[i].Id, upToMessageId, StringComparison.Ordinal))
                return messages.Take(i + 1).ToList();
        return messages;
    }
}


// ============================================================================
// FILE: AgentCore/Context/ContextLayer.cs (14 code lines, 19 total)
// ============================================================================

using AgentCore.LLM;
using AgentCore.LLM.Chat;

namespace AgentCore.Context;

public class ContextLayer(IContext? inner = null) : IContext, ILayer<IContext>
{
    public IContext Inner { get; private set; } = inner!;

    public void Attach(IContext inner) => Inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public virtual Task<IReadOnlyList<Message>> ReadAsync(CancellationToken ct = default)
        => Inner.ReadAsync(ct);

    public virtual IAsyncEnumerable<IMessageEvent> WriteAsync(
        IAsyncEnumerable<IMessageEvent> events,
        CancellationToken ct = default)
        => Inner.WriteAsync(events, ct);
}


// ============================================================================
// FILE: AgentCore/Context/Primitives/Assembler.cs (145 code lines, 171 total)
// ============================================================================

using AgentCore.LLM.Chat;
using System.Text;

namespace AgentCore.Context.Primitives;

public interface IAssembler
{
    IAssembler Create(MessageStart? start = null);
    IContent? Push(MessageDelta delta);
    Message ToMessage(MessageEnd? end = null);
}

public sealed class Assembler : IAssembler
{
    private readonly SortedDictionary<int, (IContentStart Start, StringBuilder Buffer)> _blocks = [];
    private readonly SortedDictionary<int, (ToolResultStart Start, List<IToolResultContent> Contents, StringBuilder TextBuffer)> _toolResultBlocks = [];
    private readonly List<IContent> _contents = [];
    private readonly List<IMetadata> _metadata = [];
    private Role _role = Role.Assistant;
    private string? _id;

    public IAssembler Create(MessageStart? start = null) => new Assembler
    {
        _role = start?.Role ?? Role.Assistant,
        _id = start?.Id
    };

    public IContent? Push(MessageDelta delta)
    {
        if (delta.Metadata != null) _metadata.Add(delta.Metadata);
        if (delta.Content is null) return null;

        switch (delta.Content)
        {
            case ToolResultStart trs:
                _toolResultBlocks[trs.Index] = (trs, [], new StringBuilder());
                return null;

            case ToolResultDelta trd when _toolResultBlocks.TryGetValue(trd.Index, out var tr):
                if (trd.Content is IToolResultContent c)
                {
                    if (tr.TextBuffer.Length > 0)
                    {
                        tr.Contents.Add(new Text(tr.TextBuffer.ToString()));
                        tr.TextBuffer.Clear();
                    }
                    tr.Contents.Add(c);
                }
                else if (trd.Content is TextDelta td) tr.TextBuffer.Append(td.Text);
                return null;

            case ToolResultEnd tre:
                return CompleteToolResultBlock(tre.Index, tre.IsError);

            case IContent content:
                _contents.Add(content);
                return content;

            case IContentStart s:
                _blocks[s.Index] = (s, new StringBuilder());
                return null;

            case TextDelta d when _blocks.TryGetValue(d.Index, out var b):
                b.Buffer.Append(d.Text);
                return null;

            case ReasoningDelta d when _blocks.TryGetValue(d.Index, out var b):
                b.Buffer.Append(d.Thought);
                return null;

            case ToolCallDelta d when _blocks.TryGetValue(d.Index, out var b):
                b.Buffer.Append(d.Arguments);
                return null;

            case IContentEnd end:
                return CompleteBlock(end.Index);

            default:
                return null;
        }
    }

    public Message ToMessage(MessageEnd? end = null)
    {
        if (end != null)
        {
            if (end.Id != null) _id ??= end.Id;
            CompleteAllBlocks();
            return new Message(_role, _contents, _id, _metadata);
        }

        var snapshotContents = new List<IContent>(_contents);
        foreach (var b in _blocks.Values)
        {
            if (b.Buffer.Length > 0)
                snapshotContents.Add(CreateContent(b.Start, b.Buffer.ToString()));
        }
        foreach (var tr in _toolResultBlocks.Values)
        {
            var contents = new List<IToolResultContent>(tr.Contents);
            if (tr.TextBuffer.Length > 0) contents.Add(new Text(tr.TextBuffer.ToString()));
            if (contents.Count > 0) snapshotContents.Add(new ToolResult(tr.Start.ToolCallId, contents, isError: true));
        }
        return new Message(_role, snapshotContents, _id, _metadata);
    }

    private void CompleteAllBlocks()
    {
        foreach (var index in _blocks.Keys.ToList())
        {
            CompleteBlock(index);
        }
        foreach (var index in _toolResultBlocks.Keys.ToList())
        {
            CompleteToolResultBlock(index, isError: true);
        }
    }

    private IContent? CompleteToolResultBlock(int index, bool isError = false)
    {
        if (!_toolResultBlocks.Remove(index, out var tr)) return null;
        if (tr.TextBuffer.Length > 0) tr.Contents.Add(new Text(tr.TextBuffer.ToString()));
        var res = new ToolResult(tr.Start.ToolCallId, tr.Contents, isError);
        _contents.Add(res);
        return res;
    }

    private IContent? CompleteBlock(int index)
    {
        if (!_blocks.Remove(index, out var b)) return null;
        var content = CreateContent(b.Start, b.Buffer.ToString());
        _contents.Add(content);
        return content;
    }

    private static IContent CreateContent(IContentStart start, string text) => start switch
    {
        TextStart => new Text(text),
        ReasoningStart => new Reasoning(text),
        ToolCallStart tc => new ToolCall(tc.Id, tc.Name, text),
        _ => new Text(text)
    };
}

public static class AssemblerExtensions
{
    public static async Task<IReadOnlyList<Message>> ToMessagesAsync(
        this IAsyncEnumerable<IMessageEvent> stream,
        IAssembler? assembler = null,
        CancellationToken ct = default)
    {
        var factory = assembler ?? new Assembler();
        var open = new Dictionary<string, IAssembler>(StringComparer.Ordinal);
        var messages = new List<Message>();

        await foreach (var e in stream.WithCancellation(ct).ConfigureAwait(false))
        {
            if (e is Message m) { messages.Add(m); continue; }
            var key = e.Id ?? string.Empty;
            if (e is MessageStart ms) open[key] = factory.Create(ms);
            else if (e is MessageDelta md && open.TryGetValue(key, out var asm)) asm.Push(md);
            else if (e is MessageEnd me && open.Remove(key, out var endAsm)) messages.Add(endAsm.ToMessage(me));
        }

        foreach (var remaining in open.Values)
            if (remaining.ToMessage() is { Contents.Count: > 0 } partial) messages.Add(partial);

        return messages;
    }
}



// ============================================================================
// FILE: AgentCore/Context/Primitives/Normalizer.cs (89 code lines, 101 total)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using AgentCore.LLM.Chat;

namespace AgentCore.Context.Primitives;

public interface INormalizer
{
    IReadOnlyList<Message> Normalize(IReadOnlyList<Message> messages);
}

public sealed class ChatNormalizer(
    bool ensureToolPairing = true,
    bool coalesceAdjacentRoles = true,
    bool stripPastReasoning = true) : INormalizer
{
    public IReadOnlyList<Message> Normalize(IReadOnlyList<Message> messages)
    {
        if (messages.Count == 0) return messages;

        var list = messages.ToList();
        if (ensureToolPairing) list = PairTools(list);
        if (stripPastReasoning) list = StripReasoning(list);
        if (coalesceAdjacentRoles) list = Coalesce(list);

        return list;
    }

    private static List<Message> PairTools(List<Message> list)
    {
        var toolIds = list.Where(m => m.Role == Role.Assistant)
            .SelectMany(m => m.Contents.OfType<ToolCall>().Select(c => c.Id)).ToHashSet(StringComparer.Ordinal);
        var doneIds = list.Where(m => m.Role == Role.Tool)
            .SelectMany(GetToolCallIds).Where(id => !string.IsNullOrEmpty(id)).ToHashSet(StringComparer.Ordinal);

        var result = new List<Message>(list.Count);
        foreach (var msg in list)
        {
            if (msg.Role == Role.Tool)
            {
                var ids = GetToolCallIds(msg).ToList();
                if (ids.Count > 0 && !ids.Any(toolIds.Contains)) continue;
                if (ids.Count == 0 && (string.IsNullOrEmpty(msg.Id) || !toolIds.Contains(msg.Id))) continue;
            }

            result.Add(msg);

            if (msg.Role == Role.Assistant)
            {
                foreach (var call in msg.Contents.OfType<ToolCall>().Where(c => !doneIds.Contains(c.Id)))
                {
                    result.Add(new Message(Role.Tool, [new ToolResult(call.Id, [new Text($"Tool call '{call.Name}' was aborted.")], isError: true)], call.Id));
                    doneIds.Add(call.Id);
                }
            }
        }
        return result;

        static IEnumerable<string> GetToolCallIds(Message m)
        {
            var fromResults = m.Contents.OfType<ToolResult>().Select(tr => tr.ToolCallId);
            return fromResults.Any() ? fromResults : (m.Id != null ? [m.Id] : []);
        }
    }

    private static List<Message> Coalesce(List<Message> list)
    {
        var result = new List<Message>(list.Count);
        foreach (var msg in list)
        {
            if (result.Count > 0 && result[^1].Role == msg.Role && msg.Role is Role.User or Role.Assistant)
            {
                var prev = result[^1];
                var combined = new List<IContent>(prev.Contents);
                foreach (var c in msg.Contents)
                {
                    if (c is Text t && combined.Count > 0 && combined[^1] is Text prevText)
                        combined[^1] = new Text(prevText.Value + "\n" + t.Value);
                    else
                        combined.Add(c);
                }
                result[^1] = new Message(prev.Role, combined, prev.Id, prev.Metadata);
            }
            else
            {
                result.Add(msg);
            }
        }
        return result;
    }

    private static List<Message> StripReasoning(List<Message> list)
    {
        int lastUserIdx = list.FindLastIndex(m => m.Role == Role.User);
        for (int i = 0; i < list.Count; i++)
            if (i < lastUserIdx && list[i].Role == Role.Assistant && list[i].Contents.Any(c => c is Reasoning))
                list[i] = new Message(list[i].Role, list[i].Contents.Where(c => c is not Reasoning).ToList(), list[i].Id, list[i].Metadata);
        return list;
    }
}


// ============================================================================
// FILE: AgentCore/Context/Primitives/Summarizer.cs (60 code lines, 72 total)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentCore.LLM;
using AgentCore.LLM.Chat;

namespace AgentCore.Context.Primitives;

public interface ICompactor
{
    Task<IReadOnlyList<Message>> CompactAsync(
        IReadOnlyList<Message> messages,
        int tokenLimit,
        CancellationToken ct = default);
}

public sealed class Summarizer(
    ILLM llm,
    string prompt = "Please summarize our conversation so far, focusing on key details, facts, preferences, and decisions. Keep it concise.",
    INormalizer? normalizer = null) : ICompactor
{
    private readonly ILLM _llm = llm ?? throw new ArgumentNullException(nameof(llm));
    private readonly string _prompt = prompt;
    private readonly INormalizer _normalizer = normalizer ?? new ChatNormalizer();

    public async Task<IReadOnlyList<Message>> CompactAsync(
        IReadOnlyList<Message> messages,
        int tokenLimit,
        CancellationToken ct = default)
    {
        if (messages.Count == 0) return messages;

        var request = _normalizer.Normalize([.. messages, new Message(Role.User, [new Text(_prompt)])]);

        var summaryMsgs = await _llm.GenerateAsync(request, ct: ct).ToMessagesAsync(ct: ct).ConfigureAwait(false);
        var summaryText = summaryMsgs.FirstOrDefault()?.Contents.OfType<Text>().FirstOrDefault()?.Value?.Trim() ?? string.Empty;

        return BuildCompactedHistory(messages, summaryText);
    }

    private static IReadOnlyList<Message> BuildCompactedHistory(IReadOnlyList<Message> original, string summary)
    {
        var result = new List<Message>();
        var systemMessage = original.FirstOrDefault(m => m.Role == Role.System);
        if (systemMessage != null) result.Add(systemMessage);

        result.Add(new Message(
            Role.User, 
            [new Text($"Context compacted due to overflow. Summary of previous interactions:\n{summary}")],
            metadata: [new Summary(original.Count, original.LastOrDefault()?.Id)]));

        foreach (var msg in GetTrailingTurn(original))
            if (msg.Role != Role.System)
                result.Add(msg);

        return result;
    }

    private static IReadOnlyList<Message> GetTrailingTurn(IReadOnlyList<Message> original)
    {
        if (original.Count == 0) return [];
        int i = original.Count - 1;
        if (original[i].Role == Role.Tool)
        {
            while (i > 0 && original[i].Role == Role.Tool) i--;
            return original.Skip(i).ToList();
        }
        return [original[^1]];
    }
}


// ============================================================================
// FILE: AgentCore/Context/Primitives/Tokenizer.cs (38 code lines, 43 total)
// ============================================================================

using System;
using System.Linq;
using AgentCore.LLM.Chat;

namespace AgentCore.Context.Primitives;

public interface ITokenizer
{
    int CharsPerToken { get; }
    int Estimate(IContent content);
}

public sealed class Tokenizer(
    int charsPerToken = 4,
    double pixelsPerToken = 750.0,
    int defaultImageTokens = 1000,
    int minImageTokens = 85,
    int defaultAudioTokens = 1000,
    int defaultVideoTokens = 2000) : ITokenizer
{
    public int CharsPerToken { get; } = charsPerToken > 0 ? charsPerToken : 4;

    public int Estimate(IContent content) => content switch
    {
        Text t => (int)Math.Ceiling(t.Value.Length / (double)CharsPerToken),
        Reasoning r => (int)Math.Ceiling(r.Thought.Length / (double)CharsPerToken),
        ToolCall tc => (int)Math.Ceiling((tc.Name.Length + (tc.Arguments?.Length ?? 0)) / (double)CharsPerToken),
        Image img => EstimateImage(img),
        Audio a => a.Duration is { TotalSeconds: > 0 } d ? (int)Math.Ceiling(d * 25) : defaultAudioTokens,
        Video v => v.Duration is { TotalSeconds: > 0 } d ? (int)Math.Ceiling(d * 300) : defaultVideoTokens,
        _ => 0
    };

    private int EstimateImage(Image img)
    {
        if (img.Width is > 0 && img.Height is > 0)
        {
            long pixels = (long)img.Width.Value * img.Height.Value;
            return (int)Math.Min(int.MaxValue, Math.Max(minImageTokens, Math.Ceiling(pixels / pixelsPerToken)));
        }
        return defaultImageTokens;
    }
}


// ============================================================================
// FILE: AgentCore/Context/Primitives/Truncator.cs (44 code lines, 54 total)
// ============================================================================

using System;
using System.Collections.Generic;
using AgentCore.LLM.Chat;

namespace AgentCore.Context.Primitives;

public interface ITruncator
{
    IContent Truncate(IContent content, int maxTokens);
}

public sealed class Truncator(
    ITokenizer tokenizer, 
    double headRatio = 0.5, 
    string notice = "\n... [truncated]") : ITruncator
{
    private readonly ITokenizer _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
    private readonly double _headRatio = headRatio is >= 0.0 and <= 1.0 
        ? headRatio 
        : throw new ArgumentOutOfRangeException(nameof(headRatio), "headRatio must be between 0.0 and 1.0.");
    private readonly string _notice = notice ?? string.Empty;

    public IContent Truncate(IContent content, int maxTokens)
    {
        if (_tokenizer.Estimate(content) <= maxTokens)
            return content;

        return content switch
        {
            Text t => new Text(SliceString(t.Value, maxTokens)),
            Reasoning r => new Reasoning(SliceString(r.Thought, maxTokens)),
            _ => new Text($"[{content.GetType().Name} omitted: exceeds context budget]")
        };
    }

    private string SliceString(string text, int maxTokens)
    {
        if (maxTokens <= 0) return string.Empty;

        int maxChars = maxTokens * _tokenizer.CharsPerToken;
        if (maxChars <= _notice.Length)
            return _notice[..Math.Min(_notice.Length, maxChars)];

        int available = maxChars - _notice.Length;
        if (available >= text.Length) return text;

        int head = (int)(available * _headRatio);
        int tail = available - head;

        if (head == 0) return _notice + text[^tail..];
        if (tail == 0) return text[..head] + _notice;
        return text[..head] + _notice + text[^tail..];
    }
}


// ============================================================================
// FILE: AgentCore/LLM/Chat/Content.cs (61 code lines, 69 total)
// ============================================================================

using AgentCore.LLM;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace AgentCore.LLM.Chat;

public interface IToolResultContent : IContent, IToolResultContentEvent;

public sealed class Text(string value) : IContent, IToolResultContent
{
    public string Value { get; } = value ?? "";
    public static implicit operator Text(string text) => new(text);
}

public sealed class Reasoning(string thought) : IContent
{
    public string Thought { get; } = thought ?? "";
}

public sealed class ToolCall(string id, string name, string? arguments = null) : IContent
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Arguments { get; } = arguments ?? string.Empty;
}
public static class ToolCallExtensions
{
    public static (JsonObject? Args, string? Error) ParseArguments(this ToolCall call)
    {
        if (string.IsNullOrWhiteSpace(call.Arguments)) return ([], null);
        try
        {
            return JsonNode.Parse(call.Arguments) is JsonObject obj
                ? (obj, null)
                : (null, $"Tool arguments must be a JSON object, got non-object payload: '{call.Arguments}'.");
        }
        catch (JsonException ex)
        {
            return (null, $"Invalid JSON ({ex.Message}). Raw payload: '{call.Arguments}'.");
        }
    }
}

public sealed class ToolResult(string toolCallId, IReadOnlyList<IToolResultContent> contents, bool isError = false) : IContent
{
    public string ToolCallId { get; } = toolCallId;
    public IReadOnlyList<IToolResultContent> Contents { get; } = contents ?? [];
    public bool IsError { get; } = isError;
}

public sealed record Image(
    ReadOnlyMemory<byte>? Data = null,
    Uri? Uri = null,
    string MediaType = "image/png",
    int? Width = null,
    int? Height = null) : IContent, IToolResultContent;

public sealed record Audio(
    ReadOnlyMemory<byte>? Data = null,
    Uri? Uri = null,
    string MediaType = "audio/wav",
    TimeSpan? Duration = null) : IContent, IToolResultContent;

public sealed record Video(
    ReadOnlyMemory<byte>? Data = null,
    Uri? Uri = null,
    string MediaType = "video/mp4",
    int? Width = null,
    int? Height = null,
    TimeSpan? Duration = null) : IContent, IToolResultContent;

// ============================================================================
// FILE: AgentCore/LLM/Chat/Message.cs (23 code lines, 30 total)
// ============================================================================

using AgentCore.LLM;

namespace AgentCore.LLM.Chat; 

public enum Role { System, Assistant, User, Tool }

public interface IContent : IContentEvent;

public interface IMetadata;

public sealed record TokenUsage(int InputTokens = 0, int OutputTokens = 0, int TotalTokens = 0) : IMetadata;
public sealed record Summary(int CompactedMessages = 0, string? ThroughMessageId = null) : IMetadata;

public sealed class Message(
    Role role,
    IReadOnlyList<IContent>? contents = null,
    string? id = null,
    IReadOnlyList<IMetadata>? metadata = null) : IMessageEvent
{
    public Role Role { get; } = role;
    public IReadOnlyList<IContent> Contents { get; } = contents ?? [];
    public string? Id { get; } = id;
    public IReadOnlyList<IMetadata> Metadata { get; } = metadata ?? [];
}

public static class MetadataExtensions
{
    public static T? Get<T>(this Message message) where T : class, IMetadata => message.Metadata.OfType<T>().FirstOrDefault();
    public static T? Get<T>(this IEnumerable<IMetadata>? metadata) where T : class, IMetadata => metadata?.OfType<T>().FirstOrDefault();
}


// ============================================================================
// FILE: AgentCore/LLM/ILLM.cs (12 code lines, 15 total)
// ============================================================================

using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCore.LLM;

public interface ILLM
{
    IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        CancellationToken ct = default);
}



// ============================================================================
// FILE: AgentCore/LLM/LLMExtensions.cs (40 code lines, 44 total)
// ============================================================================

namespace AgentCore.LLM;

public static class LLMExtensions
{
    public static ILLM Attach(this ILLM llm, ILLM inner)
    {
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(inner);
        if (llm is ILayer<ILLM> layer) layer.Attach(inner);
        return llm;
    }

    public static ILLM AddLayer(this ILLM llm, ILLM layer)
    {
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(layer);
        if (llm is ILayer<ILLM> head && layer is ILayer<ILLM> next)
        {
            next.Attach(head.Inner);
            head.Attach(layer);
            return llm;
        }
        if (layer is ILayer<ILLM> l) l.Attach(llm);
        return layer;
    }

    public static ILLM RemoveLayer<T>(this ILLM llm) where T : class
    {
        if (llm is T && llm is ILayer<ILLM> self) return self.Inner.RemoveLayer<T>();
        if (llm is ILayer<ILLM> head)
        {
            var newInner = head.Inner.RemoveLayer<T>();
            if (!ReferenceEquals(newInner, head.Inner)) head.Attach(newInner);
        }
        return llm;
    }

    public static TL? FindLayer<TL>(this ILLM root) where TL : class
    {
        for (var c = root; c != null; c = (c as ILayer<ILLM>)?.Inner)
            if (c is TL match) return match;
        return null;
    }
}


// ============================================================================
// FILE: AgentCore/LLM/LLMLayer.cs (23 code lines, 28 total)
// ============================================================================

using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool;

namespace AgentCore.LLM;

public delegate IAsyncEnumerable<IMessageEvent> LLMDelegate(
    IReadOnlyList<Message> messages,
    IReadOnlyList<ToolDefinition>? tools,
    JsonSchema? responseSchema,
    ILLM next,
    CancellationToken ct);

public class LLMLayer(ILLM? inner = null, LLMDelegate? handler = null) : ILLM, ILayer<ILLM>
{
    public ILLM Inner { get; private set; } = inner!;

    public void Attach(ILLM inner) => Inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public virtual IAsyncEnumerable<IMessageEvent> GenerateAsync(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        JsonSchema? responseSchema = null,
        CancellationToken ct = default)
        => handler != null
            ? handler(messages, tools, responseSchema, Inner, ct)
            : Inner.GenerateAsync(messages, tools, responseSchema, ct);
}


// ============================================================================
// FILE: AgentCore/LLM/Schema/JsonSchema.cs (28 code lines, 39 total)
// ============================================================================

using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.LLM.Schema;

public sealed class JsonSchema
{
    private readonly JsonObject _schema;
    private readonly string[] _parameterNames;
    private string? _cachedJson;

    public JsonSchema(JsonObject schema)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));

        var props = _schema["properties"] as JsonObject;
        _parameterNames = props?.Select(p => p.Key).ToArray() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> ParameterNames => _parameterNames;

    public IReadOnlyList<string> Validate(JsonNode? node, string path = "")
        => _schema.Validate(node, path);

    public void WriteTo(Utf8JsonWriter writer)
        => _schema.WriteTo(writer);

    public JsonNode ToJsonNode() => _schema.DeepClone();

    public JsonElement ToJsonElement()
    {
        using var document = JsonDocument.Parse(_schema.ToJsonString());
        return document.RootElement.Clone();
    }

    public override string ToString() => _cachedJson ??= _schema.ToString();

    public static JsonSchema For<T>() => typeof(T).GetSchemaForType();
}


// ============================================================================
// FILE: AgentCore/LLM/Schema/JsonSchemaBuilder.cs (50 code lines, 56 total)
// ============================================================================

using System.Text.Json.Nodes;

namespace AgentCore.LLM.Schema;

public static class JsonSchemaConstants
{
    public const string TypeKey = "type";
    public const string PropertiesKey = "properties";
    public const string RequiredKey = "required";
    public const string DescriptionKey = "description";
    public const string EnumKey = "enum";
    public const string AdditionalPropertiesKey = "additionalProperties";
    public const string ItemsKey = "items";
    public const string DefaultKey = "default";
}

public class JsonSchemaBuilder
{
    private readonly JsonObject _schema;

    public JsonSchemaBuilder()
    {
        _schema = new JsonObject();
    }

    public JsonSchemaBuilder(JsonSchema existingSchema)
    {
        ArgumentNullException.ThrowIfNull(existingSchema);
        _schema = (JsonObject)existingSchema.ToJsonNode();
    }

    public JsonSchemaBuilder Type(string type) { _schema[JsonSchemaConstants.TypeKey] = type; return this; }
    public JsonSchemaBuilder Type<T>() { _schema[JsonSchemaConstants.TypeKey] = typeof(T).MapClrTypeToJsonType(); return this; }
    public JsonSchemaBuilder Description(string description) { if (!string.IsNullOrWhiteSpace(description)) _schema[JsonSchemaConstants.DescriptionKey] = description; return this; }
    public JsonSchemaBuilder Enum(string[] values) { _schema[JsonSchemaConstants.EnumKey] = new JsonArray(values.Select(v => JsonValue.Create(v)).ToArray()); return this; }
    public JsonSchemaBuilder AdditionalProperties(bool allow) { _schema[JsonSchemaConstants.AdditionalPropertiesKey] = allow; return this; }
    public JsonSchemaBuilder AdditionalProperties(JsonSchema additionalProps) { _schema[JsonSchemaConstants.AdditionalPropertiesKey] = additionalProps.ToJsonNode(); return this; }
    public JsonSchemaBuilder Items(JsonSchema items) { _schema[JsonSchemaConstants.ItemsKey] = items.ToJsonNode(); return this; }
    public JsonSchemaBuilder AddProperty(string name, JsonSchema schema, bool required = true)
    {
        if (!_schema.ContainsKey(JsonSchemaConstants.PropertiesKey))
            _schema[JsonSchemaConstants.PropertiesKey] = new JsonObject();
        ((JsonObject)_schema[JsonSchemaConstants.PropertiesKey]!)[name] = schema.ToJsonNode();
        if (required)
        {
            if (!_schema.ContainsKey(JsonSchemaConstants.RequiredKey))
                _schema[JsonSchemaConstants.RequiredKey] = new JsonArray();
            ((JsonArray)_schema[JsonSchemaConstants.RequiredKey]!).Add(name);
        }
        return this;
    }
    public JsonSchemaBuilder Properties(JsonObject properties) { _schema[JsonSchemaConstants.PropertiesKey] = properties; return this; }
    public JsonSchemaBuilder Required(JsonArray required) { if (required?.Count > 0) _schema[JsonSchemaConstants.RequiredKey] = required; return this; }
    public JsonObject BuildObject() => _schema;
    public JsonSchema Build() => new JsonSchema(_schema);
}


// ============================================================================
// FILE: AgentCore/LLM/Schema/JsonSchemaExtensions.cs (169 code lines, 200 total)
// ============================================================================

using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCore.LLM.Schema;

public static class JsonSchemaExtensions
{
    private static readonly ConcurrentDictionary<Type, JsonObject> _schemaCache = new();

    public static JsonSchema GetSchemaFor<T>() => typeof(T).GetSchemaForType();

    public static JsonSchema GetSchemaForType(this Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (_schemaCache.TryGetValue(type, out var cached)) return new JsonSchema((JsonObject)cached.DeepClone());
        var result = BuildSubSchema(type, []);
        _schemaCache.TryAdd(type, (JsonObject)result.DeepClone());
        return new JsonSchema(result);
    }

    private static JsonObject BuildSubSchema(Type type, HashSet<Type> visited)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsEnum)
        {
            var typeDesc = type.GetCustomAttribute<DescriptionAttribute>()?.Description;
            return new JsonSchemaBuilder()
                .Type<string>()
                .Enum(Enum.GetNames(type))
                .Description(typeDesc ?? $"One of: {string.Join(", ", Enum.GetNames(type))}")
                .BuildObject();
        }

        if (type.IsSimpleType())
            return new JsonSchemaBuilder().Type(type.MapClrTypeToJsonType()).BuildObject();

        if (type.IsArray)
            return new JsonSchemaBuilder().Type<Array>().Items(new JsonSchema(BuildSubSchema(type.GetElementType()!, visited))).BuildObject();

        if (typeof(IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
            return new JsonSchemaBuilder().Type<Array>().Items(new JsonSchema(BuildSubSchema(type.GetGenericArguments()[0], visited))).BuildObject();

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) && type.GetGenericArguments()[0] == typeof(string))
            return new JsonSchemaBuilder().Type<Object>().AdditionalProperties(new JsonSchema(BuildSubSchema(type.GetGenericArguments()[1], visited))).BuildObject();

        if (visited.Contains(type))
            return new JsonSchemaBuilder().Type<object>().BuildObject();

        visited.Add(type);

        var props = new JsonObject();
        var required = new JsonArray();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;

            var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            var propSchema = BuildSubSchema(propType, visited);

            if (prop.GetCustomAttribute<DescriptionAttribute>() is { } descAttr && !string.IsNullOrEmpty(descAttr.Description))
                propSchema[JsonSchemaConstants.DescriptionKey] = descAttr.Description;

            if (prop.GetCustomAttribute<DefaultValueAttribute>() is { } dv)
                propSchema[JsonSchemaConstants.DefaultKey] = JsonSerializer.SerializeToNode(dv.Value!);

            props[prop.Name] = propSchema;
            if (!prop.IsOptional()) required.Add(prop.Name);
        }

        return new JsonSchemaBuilder()
            .Type<object>()
            .Properties(props)
            .Required(required)
            .AdditionalProperties(false)
            .BuildObject();
    }

    private static bool IsOptional(this PropertyInfo prop)
        => Nullable.GetUnderlyingType(prop.PropertyType) != null
        || prop.GetCustomAttribute<DefaultValueAttribute>() != null
        || IsNullableReference(prop);

    private static bool IsNullableReference(PropertyInfo prop)
    {
        return new NullabilityInfoContext().Create(prop).WriteState == NullabilityState.Nullable;
    }

    public static bool IsSimpleType(this Type type) =>
        type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(Guid);

    public static string MapClrTypeToJsonType(this Type type)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsEnum) return "string";
        if (type == typeof(string) || type == typeof(char)) return "string";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
        if (type == typeof(void) || type == typeof(DBNull)) return "null";
        if (type.IsArray || typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string)) return "array";
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
            type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte))
            return "integer";
        return "object";
    }

    public static List<string> Validate(this JsonObject schema, JsonNode? node, string path = "")
    {
        var errors = new List<string>();

        if (node == null)
        {
            if (schema["required"] is JsonArray arr && arr.Count > 0)
                errors.Add(string.IsNullOrEmpty(path) ? "Value required but missing." : $"Value required at '{path}' but missing.");
            return errors;
        }

        var type = schema["type"]?.ToString();
        var kind = node.GetValueKind();

        switch (type)
        {
            case "string":
                if (kind != JsonValueKind.String)
                    errors.Add(string.IsNullOrEmpty(path) ? $"Expected string, got {kind}." : $"Expected string at '{path}', got {kind}.");
                break;
            case "integer":
                if (kind != JsonValueKind.Number)
                    errors.Add(string.IsNullOrEmpty(path) ? $"Expected integer, got {kind}." : $"Expected integer at '{path}', got {kind}.");
                break;
            case "number":
                if (kind != JsonValueKind.Number)
                    errors.Add(string.IsNullOrEmpty(path) ? $"Expected number, got {kind}." : $"Expected number at '{path}', got {kind}.");
                break;
            case "boolean":
                if (kind != JsonValueKind.True && kind != JsonValueKind.False)
                    errors.Add(string.IsNullOrEmpty(path) ? $"Expected boolean, got {kind}." : $"Expected boolean at '{path}', got {kind}.");
                break;
            case "array":
                if (kind != JsonValueKind.Array)
                    errors.Add(string.IsNullOrEmpty(path) ? $"Expected array, got {kind}." : $"Expected array at '{path}', got {kind}.");
                else if (schema["items"] is JsonObject itemSchema)
                    for (int i = 0; i < node.AsArray().Count; i++)
                        errors.AddRange(itemSchema.Validate(node.AsArray()[i], $"{path}[{i}]"));
                break;
            case "object":
                if (kind != JsonValueKind.Object)
                    errors.Add(string.IsNullOrEmpty(path) ? $"Expected object, got {kind}." : $"Expected object at '{path}', got {kind}.");
                else if (schema["properties"] is JsonObject props)
                {
                    var objNode = node.AsObject();
                    foreach (var kvp in props)
                    {
                        var key = kvp.Key;
                        var childSchema = kvp.Value as JsonObject;

                        if (!objNode.ContainsKey(key))
                        {
                            if (schema["required"] is JsonArray reqArr && reqArr.Any(r => r?.ToString() == key))
                            {
                                var msg = string.IsNullOrEmpty(path) ? $"Missing required parameter '{key}'." : $"Missing required field '{key}' at '{path}'.";
                                errors.Add(msg);
                            }
                        }
                        else
                        {
                            if (childSchema != null)
                                errors.AddRange(childSchema.Validate(objNode[key], $"{path}.{key}".Trim('.')));
                        }
                    }

                    if (schema[JsonSchemaConstants.AdditionalPropertiesKey] is JsonValue ap
                        && ap.GetValue<bool>() == false)
                    {
                        var schemaKeys = props.Select(p => p.Key).ToHashSet();
                        foreach (var key in objNode.Select(k => k.Key))
                        {
                            if (!schemaKeys.Contains(key))
                            {
                                errors.Add(string.IsNullOrEmpty(path)
                                    ? $"Unknown parameter '{key}'. Expected parameters: [{string.Join(", ", schemaKeys)}]."
                                    : $"Unknown property '{key}' at '{path}'. Expected properties: [{string.Join(", ", schemaKeys)}].");
                            }
                        }
                    }
                }
                break;
        }

        return errors;
    }
}


// ============================================================================
// FILE: AgentCore/MessageEvents.cs (23 code lines, 26 total)
// ============================================================================

using AgentCore.LLM.Chat;

namespace AgentCore;

public interface IMessageEvent { string? Id => null; } 
public sealed record MessageStart(Role Role = Role.Assistant, string? Id = null) : IMessageEvent;
public sealed record MessageDelta(string? Id = null, IContentEvent? Content = null, IMetadata? Metadata = null) : IMessageEvent;
public sealed record MessageEnd(string? Id = null) : IMessageEvent;

public interface IContentEvent { int Index => 0; }
public interface IToolResultContentEvent : IContentEvent;
public interface IContentStart : IContentEvent;
public interface IContentDelta : IContentEvent;
public interface IContentEnd : IContentEvent; 
public sealed record TextStart(int Index = 0) : IContentStart, IToolResultContentEvent;
public sealed record TextDelta(int Index, string Text) : IContentDelta, IToolResultContentEvent;
public sealed record TextEnd(int Index = 0) : IContentEnd, IToolResultContentEvent;  
public sealed record ReasoningStart(int Index = 0) : IContentStart;
public sealed record ReasoningDelta(int Index, string Thought) : IContentDelta;
public sealed record ReasoningEnd(int Index = 0) : IContentEnd; 
public sealed record ToolCallStart(int Index, string Id, string Name) : IContentStart;
public sealed record ToolCallDelta(int Index, string Arguments) : IContentDelta;
public sealed record ToolCallEnd(int Index = 0) : IContentEnd;
public sealed record ToolResultStart(int Index, string ToolCallId) : IContentStart;
public sealed record ToolResultDelta(int Index, IToolResultContentEvent Content) : IContentDelta;
public sealed record ToolResultEnd(int Index = 0, bool IsError = false) : IContentEnd;


// ============================================================================
// FILE: AgentCore/Tool/Toolbox.cs (127 code lines, 145 total)
// ============================================================================

using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using AgentCore.Tool.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace AgentCore.Tool;
public sealed record ToolDefinition(
    string Name,
    string Description,
    JsonSchema ParametersSchema,
    IReadOnlyList<IMetadata>? Metadata = null);

public interface ITool
{
    ToolDefinition Definition { get; }
    IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(JsonObject arguments, CancellationToken ct = default);
}
public interface IToolbox
{
    ValueTask<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken ct = default);
    IAsyncEnumerable<IMessageEvent> ExecuteAsync(IReadOnlyList<ToolCall> calls, CancellationToken ct = default);
}

public sealed class Toolbox : IToolbox
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ITool> _tools;

    public IReadOnlyList<ITool> Tools => _tools.Values.ToArray();
    public ValueTask<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken ct = default) => new(_tools.Values.ToArray());
    public ILogger Logger { get; }
    public bool ParallelExecution { get; }
    public int? MaxConcurrency { get; }
    public TimeSpan? Timeout { get; }

    public Toolbox(
        IEnumerable<ITool>? tools = null,
        ILogger<Toolbox>? logger = null,
        bool parallel = true,
        int? maxConcurrency = null,
        TimeSpan? timeout = null)
    {
        Logger = logger ?? NullLogger<Toolbox>.Instance;
        ParallelExecution = parallel;
        MaxConcurrency = maxConcurrency;
        Timeout = timeout;
        _tools = new System.Collections.Concurrent.ConcurrentDictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);
        if (tools != null)
        {
            foreach (var t in tools)
                _tools[t.Definition.Name] = t;
        }
    }

    public void Add(IEnumerable<ITool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        foreach (var t in tools)
            _tools[t.Definition.Name] = t;
    }

    public bool Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _tools.TryRemove(name, out _);
    }

    public async IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (calls is not { Count: > 0 }) yield break;

        var messageId = Guid.NewGuid().ToString("N");
        yield return new MessageStart(Role.Tool, Id: messageId);

        var channel = Channel.CreateUnbounded<IMessageEvent>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = ParallelExecution ? (MaxConcurrency is > 0 and int max ? max : -1) : 1,
            CancellationToken = ct
        };

        _ = Parallel.ForEachAsync(calls.Select((call, index) => (call, index)), options, (item, token) => new ValueTask(ExecuteCallAsync(item.call, item.index, messageId, channel.Writer, token)))
            .ContinueWith(t => channel.Writer.TryComplete(t.Exception?.InnerException));

        await foreach (var evt in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return evt;

        yield return new MessageEnd(Id: messageId);
    }

    private async Task ExecuteCallAsync(ToolCall call, int index, string messageId, ChannelWriter<IMessageEvent> writer, CancellationToken ct)
    {
        var (args, parseError) = call.ParseArguments();
        if (parseError != null || string.IsNullOrWhiteSpace(call.Name) || !_tools.TryGetValue(call.Name, out var tool))
        {
            var err = parseError ?? (string.IsNullOrWhiteSpace(call.Name) ? "Tool name cannot be empty." : $"Tool '{call.Name}' not registered.");
            await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResult(call.Id, [Fail(call.Name, err)], isError: true)), ct).ConfigureAwait(false);
            return;
        }

        if (tool.Definition.ParametersSchema.Validate(args!) is { Count: > 0 } errors)
        {
            await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResult(call.Id, [Fail(call.Name, string.Join("; ", errors))], isError: true)), ct).ConfigureAwait(false);
            return;
        }

        using var cts = Timeout > TimeSpan.Zero ? CancellationTokenSource.CreateLinkedTokenSource(ct) : null;
        cts?.CancelAfter(Timeout!.Value);

        bool isError = false;
        await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResultStart(index, call.Id)), ct).ConfigureAwait(false);
        try
        {
            await foreach (var evt in tool.InvokeStreamingAsync(args!, cts?.Token ?? ct).ConfigureAwait(false))
            {
                if (evt is IToolResultContentEvent e)
                    await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResultDelta(index, e)), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cts?.IsCancellationRequested == true && !ct.IsCancellationRequested)
        {
            isError = true;
            await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResultDelta(index, Fail(call.Name, $"Tool execution timed out after {Timeout!.Value.TotalSeconds}s."))), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            isError = true;
            Logger.LogError(ex, "Tool '{Tool}' failed: {Error}", call.Name, ex.GetBaseException().Message);
            await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResultDelta(index, Fail(call.Name, ex.GetBaseException().Message))), ct).ConfigureAwait(false);
        }
        await writer.WriteAsync(new MessageDelta(messageId, Content: new ToolResultEnd(index, IsError: isError)), ct).ConfigureAwait(false);
    }

    private Text Fail(string name, string message)
    {
        Logger.LogWarning("Tool '{Tool}' error: {Error}", name, message);
        return new Text($"Error calling tool '{name}': {message}");
    }
}


// ============================================================================
// FILE: AgentCore/Tool/ToolboxExtensions.cs (71 code lines, 80 total)
// ============================================================================

using Microsoft.Extensions.Logging;

namespace AgentCore.Tool;

public static class ToolboxExtensions
{
    public static async ValueTask<IReadOnlyList<ToolDefinition>> GetDefinitionsAsync(this IToolbox toolbox, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        var tools = await toolbox.GetToolsAsync(ct).ConfigureAwait(false);
        return tools.Select(t => t.Definition).ToArray();
    }

    public static IToolbox AddTool(this IToolbox toolbox, ITool tool)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(tool);
        var target = toolbox.FindLayer<Toolbox>() ?? (toolbox as Toolbox);
        target?.Add([tool]);
        return toolbox;
    }

    public static IToolbox AddTool(this IToolbox toolbox, IEnumerable<ITool> tools)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(tools);
        var target = toolbox.FindLayer<Toolbox>() ?? (toolbox as Toolbox);
        target?.Add(tools);
        return toolbox;
    }

    public static IToolbox RemoveTool(this IToolbox toolbox, string name)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(name);
        var target = toolbox.FindLayer<Toolbox>() ?? (toolbox as Toolbox);
        target?.Remove(name);
        return toolbox;
    }

    public static IToolbox Attach(this IToolbox toolbox, IToolbox inner)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(inner);
        if (toolbox is ILayer<IToolbox> layer) layer.Attach(inner);
        return toolbox;
    }

    public static IToolbox AddLayer(this IToolbox toolbox, IToolbox layer)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(layer);
        if (toolbox is ILayer<IToolbox> head && layer is ILayer<IToolbox> next)
        {
            next.Attach(head.Inner);
            head.Attach(layer);
            return toolbox;
        }
        if (layer is ILayer<IToolbox> l) l.Attach(toolbox);
        return layer;
    }

    public static IToolbox RemoveLayer<T>(this IToolbox toolbox) where T : class
    {
        if (toolbox is T && toolbox is ILayer<IToolbox> self) return self.Inner.RemoveLayer<T>();
        if (toolbox is ILayer<IToolbox> head)
        {
            var newInner = head.Inner.RemoveLayer<T>();
            if (!ReferenceEquals(newInner, head.Inner)) head.Attach(newInner);
        }
        return toolbox;
    }

    public static TL? FindLayer<TL>(this IToolbox root) where TL : class
    {
        for (var c = root; c != null; c = (c as ILayer<IToolbox>)?.Inner)
            if (c is TL match) return match;
        return null;
    }
}


// ============================================================================
// FILE: AgentCore/Tool/ToolboxLayer.cs (19 code lines, 25 total)
// ============================================================================

using AgentCore.LLM.Chat;

namespace AgentCore.Tool;

public delegate IAsyncEnumerable<IMessageEvent> ToolboxDelegate(
    IReadOnlyList<ToolCall> calls,
    IToolbox next,
    CancellationToken ct);

public class ToolboxLayer(IToolbox? inner = null, ToolboxDelegate? handler = null) : IToolbox, ILayer<IToolbox>
{
    public IToolbox Inner { get; private set; } = inner!;

    public void Attach(IToolbox inner) => Inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public virtual ValueTask<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken ct = default)
        => Inner != null ? Inner.GetToolsAsync(ct) : new(Array.Empty<ITool>());

    public virtual IAsyncEnumerable<IMessageEvent> ExecuteAsync(
        IReadOnlyList<ToolCall> calls,
        CancellationToken ct = default)
        => handler != null
            ? handler(calls, Inner, ct)
            : Inner.ExecuteAsync(calls, ct);
}


// ============================================================================
// FILE: AgentCore/Tool/Tools/MethodTool.cs (110 code lines, 131 total)
// ============================================================================

using AgentCore.LLM;
using AgentCore.LLM.Chat;
using AgentCore.LLM.Schema;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AgentCore.Tool.Tools;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolAttribute(string? name = null, string? description = null) : Attribute
{
    public string? Name { get; } = name;
    public string? Description { get; } = description;
}

/// <summary>
/// An <see cref="ITool"/> that wraps any C# method via <see cref="MethodInfo"/> + optional target instance.
/// </summary>
public sealed class MethodTool : ITool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };

    private readonly MethodInvoker _invoker;
    private readonly object? _target;
    private readonly ParameterInfo[] _parameters;
    private readonly Func<Task, object?>? _taskResultGetter;

    public ToolDefinition Definition { get; }

    public MethodTool(MethodInfo method, object? target = null, string? name = null, string? description = null, IEnumerable<IMetadata>? extraMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!method.IsStatic && target == null)
            throw new ArgumentException("Instance methods require a target instance.", nameof(target));

        _invoker = MethodInvoker.Create(method);
        _target = target;
        _parameters = method.GetParameters();

        var metadata = method.GetCustomAttributes().OfType<IMetadata>().Concat(extraMetadata ?? []).ToList();
        Definition = new(GetName(method, name), GetDescription(method, description), BuildSchema(method), metadata.Count > 0 ? metadata : null);

        if (typeof(Task).IsAssignableFrom(method.ReturnType) && method.ReturnType.IsGenericType)
        {
            var taskParam = Expression.Parameter(typeof(Task), "task");
            var castTask = Expression.Convert(taskParam, method.ReturnType);
            var propAccess = Expression.Property(castTask, "Result");
            var castResult = Expression.Convert(propAccess, typeof(object));
            _taskResultGetter = Expression.Lambda<Func<Task, object?>>(castResult, taskParam).Compile();
        }
    }

    public async IAsyncEnumerable<IContentEvent> InvokeStreamingAsync(
        JsonObject arguments,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var args = _parameters.Length == 0 ? [] : new object?[_parameters.Length];
        for (int i = 0; i < _parameters.Length; i++)
        {
            var p = _parameters[i];
            args[i] = p.ParameterType == typeof(CancellationToken) ? ct : GetParameterValue(arguments, p);
        }

        var result = _invoker.Invoke(_target, args);

        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            result = _taskResultGetter != null
                ? _taskResultGetter(task)
                : (task.GetType().IsGenericType ? task.GetType().GetProperty("Result")?.GetValue(task) : null);
        }

        var contents = ToContentList(result);
        for (int i = 0; i < contents.Count; i++)
            yield return contents[i];
    }

    private static object? GetParameterValue(JsonObject obj, ParameterInfo p)
    {
        if (obj.TryGetPropertyValue(p.Name!, out var node) && node != null)
            return JsonSerializer.Deserialize(node, p.ParameterType, JsonOptions);

        return p.HasDefaultValue ? p.DefaultValue : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null);
    }

    private static IReadOnlyList<IContent> ToContentList(object? raw) => raw switch
    {
        null => [new Text(string.Empty)],
        IContent c => [c],
        IReadOnlyList<IContent> list => list,
        IEnumerable<IContent> enumContents => enumContents.ToList(),
        string s => [new Text(s)],
        _ => [new Text(JsonSerializer.Serialize(raw))]
    };

    private static string GetName(MethodInfo method, string? name) =>
        !string.IsNullOrWhiteSpace(name) ? name : method.GetCustomAttribute<ToolAttribute>()?.Name ?? method.Name;

    private static string GetDescription(MethodInfo method, string? description) =>
        description
        ?? method.GetCustomAttribute<ToolAttribute>()?.Description
        ?? method.GetCustomAttribute<DescriptionAttribute>()?.Description
        ?? GetName(method, null);

    private static JsonSchema BuildSchema(MethodInfo method)
    {
        var builder = new JsonSchemaBuilder().Type<object>().AdditionalProperties(false);
        builder.Properties(new JsonObject());

        foreach (var p in method.GetParameters())
        {
            if (p.ParameterType == typeof(CancellationToken)) continue;
            var desc = p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? p.Name!;
            var paramSchema = new JsonSchemaBuilder(p.ParameterType.GetSchemaForType()).Description(desc).Build();
            builder.AddProperty(p.Name!, paramSchema, required: !p.IsOptional);
        }

        return builder.Build();
    }
}


// ============================================================================
// FILE: AgentCore/Tool/Tools/MethodToolExtensions.cs (25 code lines, 31 total)
// ============================================================================

using AgentCore.LLM.Chat;
using System.Reflection;

namespace AgentCore.Tool.Tools;

public static class MethodToolExtensions
{
    public static IToolbox AddTool<T>(this IToolbox toolbox, params IMetadata[] metadata)
        => toolbox.AddTool(typeof(T), null, metadata);

    public static IToolbox AddTool(this IToolbox toolbox, object instance, params IMetadata[] metadata)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentNullException.ThrowIfNull(instance);
        return instance is ITool tool
            ? toolbox.AddTool([tool])
            : toolbox.AddTool(instance.GetType(), instance, metadata);
    }

    private static IToolbox AddTool(this IToolbox toolbox, Type type, object? target, IMetadata[] metadata)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        var flags = BindingFlags.Public | BindingFlags.Static | (target != null ? BindingFlags.Instance : 0);

        var tools = type.GetMethods(flags)
            .Where(m => m.GetCustomAttribute<ToolAttribute>() != null)
            .Select(m => (ITool)new MethodTool(m, m.IsStatic ? null : target, extraMetadata: metadata));

        return toolbox.AddTool(tools);
    }
}


