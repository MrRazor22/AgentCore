using SharpToken;

namespace AgentCore.Tokens
{
    public interface ITokenCounter
    {
        int Count(string payload);
    }

    public sealed class SharpTokenCounter : ITokenCounter
    {
        private readonly GptEncoding _encoding;

        public SharpTokenCounter(string model)
        {
            _encoding = GptEncoding.GetEncoding(ResolveEncoding(model));
        }

        private static string ResolveEncoding(string model)
            => "cl100k_base"; // OpenAI chat models

        public int Count(string payload)
        {
            if (string.IsNullOrEmpty(payload))
                return 0;

            return _encoding.Encode(payload).Count;
        }
    }

}
