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

public static class TensorSharpExtensions
{
    public static TensorSharpLLM CreateTensorSharpLLM(
        InferenceEngine engine,
        IModelArchitecture model,
        SamplingConfig? samplingConfig = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(model);
        return new TensorSharpLLM(engine, model, samplingConfig);
    }

    public static TensorSharpLLM CreateTensorSharpLLM(
        string ggufPath,
        BackendType? backend = null,
        SamplingConfig? samplingConfig = null)
    {
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

        return new TensorSharpLLM(engine, model, samplingConfig);
    }

    public static Agent UseTensorSharp(
        this Agent agent,
        InferenceEngine engine,
        IModelArchitecture model,
        SamplingConfig? samplingConfig = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return agent.With(llm: CreateTensorSharpLLM(engine, model, samplingConfig));
    }

    public static Agent UseTensorSharp(
        this Agent agent,
        string ggufPath,
        BackendType? backend = null,
        SamplingConfig? samplingConfig = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return agent.With(llm: CreateTensorSharpLLM(ggufPath, backend, samplingConfig));
    }
}
