using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;

namespace TestApp.Services;

public abstract record ShowcaseEvent
{
    public required string SessionId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public virtual string Type { get; init; } = "";
}

// UI Chat Feed Events
public record MessageStartedEvent : ShowcaseEvent { public override string Type { get; init; } = "MessageStarted"; }
public record ReasoningDeltaEvent(string Delta) : ShowcaseEvent { public override string Type { get; init; } = "ReasoningDelta"; }
public record TextDeltaEvent(string Delta) : ShowcaseEvent { public override string Type { get; init; } = "TextDelta"; }
public record ToolCallStartedEvent(string CallId, string ToolName, string Arguments) : ShowcaseEvent { public override string Type { get; init; } = "ToolCallStarted"; }
public record ToolResultEvent(string CallId, string ToolName, string Result) : ShowcaseEvent { public override string Type { get; init; } = "ToolResult"; }
public record ApprovalRequestedEvent(string CallId, string ToolName, string Arguments) : ShowcaseEvent { public override string Type { get; init; } = "ApprovalRequested"; }
public record CompletedEvent : ShowcaseEvent { public override string Type { get; init; } = "Completed"; }
public record ErrorEvent(string Error) : ShowcaseEvent { public override string Type { get; init; } = "Error"; }

// Observability/Pipeline Events
public record PipelineStageEvent(string StageName, string Status) : ShowcaseEvent { public override string Type { get; init; } = "PipelineStage"; }
public record PromptBuiltEvent(string PromptText) : ShowcaseEvent { public override string Type { get; init; } = "PromptBuilt"; }
public record LLMRequestStartedEvent(string Model) : ShowcaseEvent { public override string Type { get; init; } = "LLMRequestStarted"; }
public record LLMResponseReceivedEvent(int InputTokens, int OutputTokens, double ElapsedMs) : ShowcaseEvent { public override string Type { get; init; } = "LLMResponseReceived"; }
public record ToolInvokingEvent(string ToolName, string Arguments) : ShowcaseEvent { public override string Type { get; init; } = "ToolInvoking"; }
public record ToolCompletedEvent(string ToolName, string Result) : ShowcaseEvent { public override string Type { get; init; } = "ToolCompleted"; }
public record MemoryUpdatedEvent(int HistoryCount) : ShowcaseEvent { public override string Type { get; init; } = "MemoryUpdated"; }

public interface IEventPublisher
{
    void Publish(ShowcaseEvent @event);
}

public interface IAgentEventBus : IEventPublisher
{
    IAsyncEnumerable<ShowcaseEvent> Subscribe(string sessionId, CancellationToken ct);
}

public class AgentEventBus : IAgentEventBus
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<Channel<ShowcaseEvent>>> _subscriptions = new();

    public void Publish(ShowcaseEvent @event)
    {
        if (_subscriptions.TryGetValue(@event.SessionId, out var bags))
        {
            foreach (var channel in bags)
            {
                channel.Writer.TryWrite(@event);
            }
        }
    }

    public async IAsyncEnumerable<ShowcaseEvent> Subscribe(string sessionId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<ShowcaseEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var bag = _subscriptions.GetOrAdd(sessionId, _ => new ConcurrentBag<Channel<ShowcaseEvent>>());
        bag.Add(channel);

        try
        {
            while (await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var @event))
                {
                    yield return @event;
                }
            }
        }
        finally
        {
            if (_subscriptions.TryGetValue(sessionId, out var currentBag))
            {
                var newBag = new ConcurrentBag<Channel<ShowcaseEvent>>(currentBag.Where(c => c != channel));
                _subscriptions.TryUpdate(sessionId, newBag, currentBag);
            }
        }
    }
}
