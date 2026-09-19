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
