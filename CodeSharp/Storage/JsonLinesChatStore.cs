namespace CodeSharp.Storage;

public sealed class JsonLinesChatStore(string storageDirectory) 
    : AgentCore.Layers.Chat.JsonLinesChatStore(storageDirectory);
