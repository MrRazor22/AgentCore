using System;
using System.Collections.Generic;
using AgentCore;
using AgentCore.LLM;
using AgentCore.LLM.TensorSharp;
using TensorSharp;
using TensorSharp.Models;
using TensorSharp.Runtime;
using TensorSharp.Runtime.Scheduling;

namespace AgentCore.LLM.TensorSharp;

public static class TensorSharpBuilderExtensions
{
    /// <summary>
    /// Configures the LLMBuilder to use an in-process TensorSharp InferenceEngine.
    /// </summary>
    public static LLMBuilder WithTensorSharp(
        this LLMBuilder builder,
        InferenceEngine engine,
        IModelArchitecture model,
        SamplingConfig? samplingConfig = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(model);

        return builder.Use(_ => new TensorSharpLLM(engine, model, samplingConfig));
    }

    /// <summary>
    /// Loads a local GGUF model via TensorSharp with natural backend fallback (GgmlCuda -> Cuda -> Cpu) and attaches it to LLMBuilder.
    /// </summary>
    public static LLMBuilder WithTensorSharpModel(
        this LLMBuilder builder,
        string ggufPath,
        BackendType? backend = null,
        SamplingConfig? samplingConfig = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (string.IsNullOrWhiteSpace(ggufPath))
            throw new ArgumentException("Model path cannot be null or empty.", nameof(ggufPath));

        var candidates = backend.HasValue
            ? [backend.Value]
            : new[] { BackendType.GgmlCuda, BackendType.Cuda, BackendType.Cpu };

        ModelBase? model = null;
        var failureReasons = new List<string>();

        foreach (var candidate in candidates)
        {
            try
            {
                Console.WriteLine($"[TensorSharp] Attempting backend '{candidate}'...");
                model = ModelBase.Create(ggufPath, candidate);
                Console.WriteLine($"[TensorSharp] Successfully initialized backend '{candidate}'.");
                break;
            }
            catch (Exception ex)
            {
                var msg = $"[{candidate}] {ex.GetType().Name}: {ex.Message}";
                failureReasons.Add(msg);
                Console.Error.WriteLine($"[TensorSharp] Backend '{candidate}' failed: {ex.Message}");

                if (backend.HasValue)
                    throw;
            }
        }

        if (model == null)
            throw new InvalidOperationException($"Failed to load model '{ggufPath}' on any backend. Attempts:\n- {string.Join("\n- ", failureReasons)}");

        var schedulerConfig = SchedulerConfig.FromEnvironment();
        var engine = new InferenceEngine(model, schedulerConfig);

        return builder.WithTensorSharp(engine, model, samplingConfig);
    }

    /// <summary>
    /// Configures the AgentBuilder to use an in-process TensorSharp InferenceEngine via UseLLM.
    /// </summary>
    public static AgentBuilder WithTensorSharp(
        this AgentBuilder builder,
        InferenceEngine engine,
        IModelArchitecture model,
        SamplingConfig? samplingConfig = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseLLM(llm => llm.WithTensorSharp(engine, model, samplingConfig));
    }

    /// <summary>
    /// Loads a local GGUF model via TensorSharp with natural backend fallback and attaches it to AgentBuilder via UseLLM.
    /// </summary>
    public static AgentBuilder WithTensorSharpModel(
        this AgentBuilder builder,
        string ggufPath,
        BackendType? backend = null,
        SamplingConfig? samplingConfig = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseLLM(llm => llm.WithTensorSharpModel(ggufPath, backend, samplingConfig));
    }
}
