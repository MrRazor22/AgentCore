namespace AgentCore;

public interface ILayer<T>
{
    T Inner { get; }
    void Attach(T inner);
}
