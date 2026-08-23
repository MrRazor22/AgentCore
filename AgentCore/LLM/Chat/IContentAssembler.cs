namespace AgentCore.LLM.Chat;

/// <summary>
/// Demultiplexes, routes, and accumulates streaming chunks to modality builders,
/// yielding settled <see cref="IContent"/> items as they complete.
/// </summary>
public interface IContentAssembler
{
    /// <summary>
    /// Ingests an incoming <see cref="StreamChunk"/>, routing it to the appropriate modality builder,
    /// and yields any completed or streaming <see cref="IContent"/> items.
    /// </summary>
    IEnumerable<IContent> Receive(StreamChunk chunk);
}
