using System;

namespace AgentCore.LLM;

/// <summary>
/// Thrown when the LLM provider rejects a request because the input context exceeds the model's maximum context length.
/// </summary>
public class ContextLengthExceededException : Exception
{
    public ContextLengthExceededException() 
        : base("The request exceeded the maximum context length allowed by the provider.")
    {
    }

    public ContextLengthExceededException(string message) 
        : base(message)
    {
    }

    public ContextLengthExceededException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}
