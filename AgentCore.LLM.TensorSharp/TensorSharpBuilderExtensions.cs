using System;
using AgentCore;
using AgentCore.LLM.TensorSharp;
using TensorSharp;
using TensorSharp.Models;
using TensorSharp.Runtime;
using TensorSharp.Runtime.Scheduling;

namespace AgentCore;

public static class TensorSharpBuilderExtensions
{
    /// <summary>
    /// Configures the Agent.Builder to use an in-process TensorSharp InferenceEngine.
    /// </summary>
    public static Agent.Builder WithTensorSharp(
        this Agent.Builder builder,
        InferenceEngine engine,
        IModelArchitecture model,
        SamplingConfig? samplingConfig = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(model);

        return builder.WithLLM(_ => new TensorSharpLLM(engine, model, samplingConfig));
    }

    /// <summary>
    /// Loads a local GGUF model via TensorSharp on the specified backend (defaulting to GgmlCuda) and attaches it to the Agent.Builder.
    /// </summary>
    /// <param name="builder">The Agent.Builder instance.</param>
    /// <param name="ggufPath">The full path to the .gguf model file.</param>
    /// <param name="backend">The compute backend: GgmlCuda (for NVIDIA GPUs like RTX 3060), GgmlVulkan, GgmlMetal, or Cpu.</param>
    /// <param name="samplingConfig">Optional sampling parameters (temperature, top_p, max_tokens, etc.).</param>
    public static Agent.Builder WithTensorSharpModel(
        this Agent.Builder builder,
        string ggufPath,
        BackendType backend = BackendType.GgmlCuda,
        SamplingConfig? samplingConfig = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (string.IsNullOrWhiteSpace(ggufPath))
            throw new ArgumentException("Model path cannot be null or empty.", nameof(ggufPath));

        var model = ModelBase.Create(ggufPath, backend);
        var schedulerConfig = SchedulerConfig.FromEnvironment();
        var engine = new InferenceEngine(model, schedulerConfig);

        return builder.WithTensorSharp(engine, model, samplingConfig);
    }
}
