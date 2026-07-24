namespace AgentCore.LLM.Exceptions;

/// <summary>
/// Thrown when the LLM provider rejects a request because the input context exceeds the model's maximum context length.
/// </summary>
public class ContextLengthExceededException : Exception
{
    public ContextLengthExceededException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
