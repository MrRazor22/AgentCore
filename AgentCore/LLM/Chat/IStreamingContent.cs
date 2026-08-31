namespace AgentCore.LLM.Chat;
 
public interface IStreamingContent
{
    IContent ToContent();
}

public interface IStreamingContent<in TDelta> : IStreamingContent
{
    void Append(TDelta chunk);
}
