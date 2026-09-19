# Project Structure

```text
AgentCore-Main/
├── AgentCore/
│   ├── Context/
│   │   ├── Primitives/
│   │   │   ├── Assembler.cs
│   │   │   ├── Normalizer.cs
│   │   │   ├── Summarizer.cs
│   │   │   ├── Tokenizer.cs
│   │   │   └── Truncator.cs
│   │   ├── ChatContext.cs
│   │   ├── ContextExtensions.cs
│   │   └── ContextLayer.cs
│   ├── LLM/
│   │   ├── Chat/
│   │   │   ├── Content.cs
│   │   │   └── Message.cs
│   │   ├── Schema/
│   │   │   ├── JsonSchema.cs
│   │   │   ├── JsonSchemaBuilder.cs
│   │   │   └── JsonSchemaExtensions.cs
│   │   ├── ILLM.cs
│   │   ├── LLMLayer.cs
│   │   └── LLMLayerExtensions.cs
│   ├── Tool/
│   │   ├── InternalTools/
│   │   ├── Tools/
│   │   │   ├── MethodTool.cs
│   │   │   └── MethodToolExtensions.cs
│   │   ├── ToolCallExtensions.cs
│   │   ├── ToolExtensions.cs
│   │   ├── Tooling.cs
│   │   └── ToolingLayer.cs
│   ├── Agent.cs
│   ├── AgentCore.csproj
│   ├── AgentExtensions.cs
│   └── Events.cs
├── AgentCore.Layers/
│   ├── Chat/
│   │   ├── Store/
│   │   │   ├── ChatStore.cs
│   │   │   ├── StoreJson.cs
│   │   │   └── WalStore.cs
│   │   ├── ChatPersistenceLayer.cs
│   │   └── ContextLayerExtensions.cs
│   ├── LLM/
│   │   ├── LLMLayerExtensions.cs
│   │   ├── RetryLayer.cs
│   │   ├── StreamingEventLayer.cs
│   │   └── ToolCallDetectionLayer.cs
│   ├── Tool/
│   │   ├── ToolApprovalLayer.cs
│   │   ├── ToolDiscoveryLayer.cs
│   │   └── ToolingLayerExtensions.cs
│   └── AgentCore.Layers.csproj
├── AgentCore.LLM.Tornado/
│   ├── AgentCore.LLM.Tornado.csproj
│   ├── TornadoAdapterExtensions.cs
│   ├── TornadoBuilderExtensions.cs
│   └── TornadoLLM.cs
```
