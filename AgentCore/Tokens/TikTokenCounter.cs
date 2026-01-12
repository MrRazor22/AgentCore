using Microsoft.Extensions.Logging;
using SharpToken;
using System;

namespace AgentCore.Tokens
{
    public interface ITokenCounter
    {
        int Count(string payload);
    }
    public sealed class TikTokenCounter : ITokenCounter
    {
        private readonly GptEncoding _encoding;

        public TikTokenCounter(string encodingName)
        {
            _encoding = GptEncoding.GetEncoding(encodingName);
        }

        public int Count(string payload)
        {
            if (string.IsNullOrEmpty(payload))
                return 0;

            return _encoding.Encode(payload).Count;
        }
    }

}
