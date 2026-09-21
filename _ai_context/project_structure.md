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
│   │   ├── LLMExtensions.cs
│   │   └── LLMLayer.cs
│   ├── Tool/
│   │   ├── InternalTools/
│   │   ├── Tools/
│   │   │   ├── MethodTool.cs
│   │   │   └── MethodToolExtensions.cs
│   │   ├── Toolbox.cs
│   │   ├── ToolboxExtensions.cs
│   │   └── ToolboxLayer.cs
│   ├── Agent.cs
│   ├── AgentCore.csproj
│   ├── AgentExtensions.cs
│   └── MessageEvents.cs
├── AgentCore.Layers/
│   ├── Chat/
│   │   ├── Store/
│   │   │   ├── ChatStore.cs
│   │   │   ├── StoreJson.cs
│   │   │   └── WalStore.cs
│   │   ├── ChatPersistenceLayer.cs
│   │   └── ContextLayerExtensions.cs
│   ├── LLM/
│   │   ├── InputGuardrailLayer.cs
│   │   ├── LLMLayerExtensions.cs
│   │   ├── RetryLayer.cs
│   │   └── ToolCallDetectionLayer.cs
│   ├── Tool/
│   │   ├── ToolApprovalLayer.cs
│   │   ├── ToolDiscoveryLayer.cs
│   │   └── ToolingLayerExtensions.cs
│   └── AgentCore.Layers.csproj
├── AgentCore.LLM.Tornado/
│   ├── AgentCore.LLM.Tornado.csproj
│   ├── TornadoAdapterExtensions.cs
│   └── TornadoLLM.cs
├── AgentCore.MCP/
│   ├── AgentCore.MCP.csproj
│   ├── McpTool.cs
│   └── McpToolBuilderExtensions.cs
```
