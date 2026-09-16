using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AgentCore;
using AgentCore.LLM.Chat;

namespace AgentCore.Layers.Context.Store;

public static class StoreJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { ConfigurePolymorphism }
        },
        Converters = { new MetadataConverter() }
    };

    private static void ConfigurePolymorphism(JsonTypeInfo ti)
    {
        ti.PolymorphismOptions = ti.Type switch
        {
            _ when ti.Type == typeof(IMessageEvent) => Polymorphic(
                (typeof(MessageStart), "msg_start"),
                (typeof(MessageDelta), "msg_delta"),
                (typeof(MessageEnd), "msg_end"),
                (typeof(Message), "message")),

            _ when ti.Type == typeof(IContentEvent) => Polymorphic(
                (typeof(TextStart), "text_start"),
                (typeof(TextDelta), "text_delta"),
                (typeof(TextEnd), "text_end"),
                (typeof(ReasoningStart), "reasoning_start"),
                (typeof(ReasoningDelta), "reasoning_delta"),
                (typeof(ReasoningEnd), "reasoning_end"),
                (typeof(ToolCallStart), "tool_start"),
                (typeof(ToolCallDelta), "tool_delta"),
                (typeof(ToolCallEnd), "tool_end"),
                (typeof(Text), "text"),
                (typeof(Reasoning), "reasoning"),
                (typeof(ToolCall), "tool_call"),
                (typeof(Image), "image")),

            _ when ti.Type == typeof(IContent) => Polymorphic(
                (typeof(Text), "text"),
                (typeof(Reasoning), "reasoning"),
                (typeof(ToolCall), "tool_call"),
                (typeof(Image), "image")),

            _ => ti.PolymorphismOptions
        };
    }

    private static JsonPolymorphismOptions Polymorphic(params (Type Type, string Name)[] types)
    {
        var options = new JsonPolymorphismOptions { TypeDiscriminatorPropertyName = "$type" };
        foreach (var (type, name) in types)
            options.DerivedTypes.Add(new JsonDerivedType(type, name));
        return options;
    }
}

internal sealed class MetadataConverter : JsonConverter<IMetadata>
{
    private static readonly ConcurrentDictionary<string, Type?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public override IMetadata? Read(ref Utf8JsonReader r, Type _, JsonSerializerOptions o)
    {
        using var doc = JsonDocument.ParseValue(ref r);
        var tag = doc.RootElement.TryGetProperty("$type", out var p) ? p.GetString() : null;
        var type = tag != null ? Cache.GetOrAdd(tag, t => Type.GetType(t) is { } found && typeof(IMetadata).IsAssignableFrom(found) ? found : null) : null;
        return type != null ? (IMetadata?)doc.RootElement.Deserialize(type, o) : null;
    }

    public override void Write(Utf8JsonWriter w, IMetadata v, JsonSerializerOptions o)
    {
        using var doc = JsonSerializer.SerializeToDocument(v, v.GetType(), o);
        w.WriteStartObject();
        w.WriteString("$type", $"{v.GetType().FullName}, {v.GetType().Assembly.GetName().Name}");
        foreach (var p in doc.RootElement.EnumerateObject())
            if (!p.NameEquals("$type")) p.WriteTo(w);
        w.WriteEndObject();
    }
}
