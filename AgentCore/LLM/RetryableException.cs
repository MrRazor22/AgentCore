using System;

namespace AgentCore.LLM;

/// <summary>
/// Thrown when a transient failure occurs.
/// This exception indicates that retrying the same request without modification is safe.
/// </summary>
public class RetryableException : Exception
{
    public RetryableException(string message, Exception? innerException = null) 
        : base(message, innerException)
    {
    }
}
